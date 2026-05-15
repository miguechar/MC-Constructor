using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCConstructor
{
    /// <summary>
    /// A part queued for nesting. Carries the source entity's bbox so the
    /// guillotine layout can place it, plus a Wblock'd .dwg blob of the
    /// real geometry so the nest output renders curves/holes/etc. faithfully
    /// instead of replacing every part with a bounding rectangle.
    /// </summary>
    public class NestingPart
    {
        public ObjectId SourceObjectId { get; set; }
        public string PartName { get; set; }

        /// <summary>True when the source had MC XData. When false, we
        /// place the geometry but skip name/dimension labels.</summary>
        public bool HasMetadata { get; set; }

        // Width/Height represent the post-rotation footprint - the layout
        // algorithm swaps them when it picks the rotated orientation.
        public double Width { get; set; }
        public double Height { get; set; }
        public double Thickness { get; set; }

        public Point3d OriginalMin { get; set; }
        public Point3d OriginalMax { get; set; }

        public double NestedX { get; set; }
        public double NestedY { get; set; }
        /// <summary>True when the layout chose to rotate this part 90deg CCW.</summary>
        public bool IsRotated { get; set; }
        public bool IsPlaced { get; set; }

        /// <summary>
        /// Matrix to orient the part so it lays flat (smallest dimension becomes Z/thickness).
        /// Applied before the layout transform. Identity if the part is already well-oriented.
        /// </summary>
        public Matrix3d LayFlatRotation { get; set; } = Matrix3d.Identity;

        /// <summary>
        /// Wblock'd .dwg bytes captured at extract time. Lets the nest output
        /// reproduce the exact source geometry (3D solid, polyline, region,
        /// circle, etc.) at the layout-chosen position. Null when
        /// serialization failed - we fall back to drawing a bounding rect.
        /// </summary>
        public byte[] GeometryDwgBytes { get; set; }
    }

    public class NestingResult
    {
        public List<NestingPart> PlacedParts { get; set; } = new List<NestingPart>();
        public List<NestingPart> UnplacedParts { get; set; } = new List<NestingPart>();
        public double PlateWidth { get; set; }
        public double PlateHeight { get; set; }
        public double UsedArea { get; set; }
        public double Efficiency { get; set; }
    }

    public static class NestingService
    {
        public const double DEFAULT_SPACING = 10.0;

        /// <summary>
        /// Analyze the three dimensions of a part and determine the rotation
        /// needed to lay it flat with the smallest dimension as thickness.
        /// Returns the rotation matrix and the reoriented dimensions
        /// (width = actual X-extent after rotation, height = actual Y-extent
        /// after rotation, thickness = actual Z-extent after rotation).
        ///
        /// IMPORTANT: width and height correspond to the *real* axis extents
        /// after lay-flat rotation - they are NOT sorted. Sorting them broke
        /// 2D shapes (hexagons, irregular polylines) where the bbox X-extent
        /// is larger than the Y-extent: the layout would think the part was
        /// "narrow tall" while the actual entity was "wide short", placing
        /// it in a cell of the wrong aspect ratio and causing overlaps with
        /// neighbouring parts.
        /// </summary>
        private static (Matrix3d rotation, double width, double height, double thickness)
            ComputeLayFlatOrientation(Point3d min, Point3d max)
        {
            double dX = Math.Abs(max.X - min.X);
            double dY = Math.Abs(max.Y - min.Y);
            double dZ = Math.Abs(max.Z - min.Z);

            // Pick the thinnest axis - that's the dimension we want pointing
            // up (Z) after the lay-flat rotation. Tie-break in favour of Z so
            // 2D shapes (where dZ is already 0 or near zero) need no rotation.
            int thinnestAxis;
            if (dZ <= dX && dZ <= dY)
                thinnestAxis = 2;
            else if (dX <= dY)
                thinnestAxis = 0;
            else
                thinnestAxis = 1;

            Matrix3d rotation;
            double width, height, thickness;

            if (thinnestAxis == 2)
            {
                // Z is already the smallest - no rotation needed.
                // Preserve the actual XY extents exactly as drawn so the
                // layout cell size matches the rendered footprint.
                rotation = Matrix3d.Identity;
                width = dX;
                height = dY;
                thickness = dZ;
            }
            else if (thinnestAxis == 0)
            {
                // X is thinnest - rotate +90deg about Y axis to bring X to Z.
                // After rotation: X-extent = dZ, Y-extent = dY, Z-extent = dX.
                rotation = Matrix3d.Rotation(Math.PI / 2, Vector3d.YAxis, min);
                width = dZ;
                height = dY;
                thickness = dX;
            }
            else
            {
                // Y is thinnest - rotate -90deg about X axis to bring Y to Z.
                // After rotation: X-extent = dX, Y-extent = dZ, Z-extent = dY.
                rotation = Matrix3d.Rotation(-Math.PI / 2, Vector3d.XAxis, min);
                width = dX;
                height = dZ;
                thickness = dY;
            }

            return (rotation, width, height, thickness);
        }

        /// <summary>
        /// Pull NestingParts out of a selection set. Accepts any entity with
        /// a usable XY bounding box - 3D solids, polylines, regions,
        /// circles, ellipses, 2D solids, splines, etc. - so the user can
        /// nest 2D shapes directly without round-tripping through MC part
        /// metadata. When a part has MC XData, the part name is captured
        /// and HasMetadata is set so the nest output knows to draw a label.
        /// </summary>
        public static List<NestingPart> ExtractPartsFromSelection(SelectionSet selection)
        {
            var parts = new List<NestingPart>();
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in selection)
                {
                    var entity = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                    if (entity == null) continue;

                    // Need extents to plan the layout. A few entity types
                    // throw when called outside a valid context; just skip
                    // them.
                    Extents3d ext;
                    try { ext = entity.GeometricExtents; }
                    catch { continue; }

                    double width  = ext.MaxPoint.X - ext.MinPoint.X;
                    double depth  = ext.MaxPoint.Y - ext.MinPoint.Y;
                    double height = ext.MaxPoint.Z - ext.MinPoint.Z;
                    if (width <= 1e-6 || depth <= 1e-6) continue;

                    // Detect MC metadata. No metadata = no labels in the nest
                    // output (per "just arrange and place" workflow).
                    string partName = "";
                    bool hasMetadata = false;
                    try
                    {
                        var xdata = entity.GetXDataForApplication(Commands.AppName);
                        if (xdata != null)
                        {
                            var values = xdata.AsArray();
                            for (int i = 1; i < values.Length - 1; i += 2)
                            {
                                if (values[i].Value.ToString() == "PartName")
                                {
                                    string name = values[i + 1].Value.ToString();
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        partName = name;
                                        hasMetadata = true;
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    catch { }

                    // Compute the orientation needed to lay the part flat
                    // (smallest dimension becomes thickness/Z)
                    var (layFlatRot, nestWidth, nestHeight, nestThickness) =
                        ComputeLayFlatOrientation(ext.MinPoint, ext.MaxPoint);

                    parts.Add(new NestingPart
                    {
                        SourceObjectId = selObj.ObjectId,
                        PartName = partName,
                        HasMetadata = hasMetadata,
                        Width = nestWidth,
                        Height = nestHeight,
                        Thickness = nestThickness,
                        OriginalMin = ext.MinPoint,
                        OriginalMax = ext.MaxPoint,
                        LayFlatRotation = layFlatRot
                    });
                }

                tr.Commit();
            }

            // Serialize each part's real geometry to a Wblock'd .dwg blob.
            // Done outside the read transaction because Database.Wblock
            // starts its own. Best-effort: a part that fails to serialize
            // simply renders as a bounding rectangle in the output.
            foreach (var part in parts)
            {
                try
                {
                    part.GeometryDwgBytes = PartGeometryHelper.SerializeEntityToDwgBytes(part.SourceObjectId);
                }
                catch
                {
                    // GeometryDwgBytes stays null - WriteNestGeometryToModelSpace
                    // will draw a fallback rectangle for this part.
                }
            }

            return parts;
        }

        /// <summary>
        /// Pack parts onto the plate using the MAXRECTS algorithm with multi-strategy
        /// optimization. Tries several sort orders and scoring heuristics, then keeps
        /// the layout with the most parts placed and best area utilization.
        ///
        /// MAXRECTS tracks the set of MAXIMAL free rectangles (rather than guillotine
        /// splits), which lets later parts use space the previous part didn't fully
        /// claim. Combined with rotation testing per part, this fits irregular sets
        /// of parts much tighter than the naive single-pass algorithm.
        ///
        /// Method name kept for API compatibility with existing callers.
        /// </summary>
        public static NestingResult NestPartsGuillotine(List<NestingPart> parts, double plateWidth, double plateHeight, double spacing = DEFAULT_SPACING)
        {
            if (parts == null || parts.Count == 0)
            {
                return new NestingResult { PlateWidth = plateWidth, PlateHeight = plateHeight };
            }

            // Try multiple sort orders and pick the best result. Different orderings
            // give very different packings - sorting by area, longest side, height,
            // and width all dominate in different scenarios. Best-of-N is cheap and
            // robust for the small part counts typical in CAD nesting.
            var sortStrategies = new Func<NestingPart, double>[]
            {
                p => -(p.Width * p.Height),         // by area, descending
                p => -Math.Max(p.Width, p.Height),  // by longest side, descending
                p => -(p.Width + p.Height),         // by perimeter, descending
                p => -p.Height,                      // by height, descending
                p => -p.Width                        // by width, descending
            };

            // Try multiple fit-scoring heuristics too
            var scoringHeuristics = new[] { FitHeuristic.BestShortSide, FitHeuristic.BestLongSide, FitHeuristic.BestArea };

            NestingResult bestResult = null;

            foreach (var sortKey in sortStrategies)
            {
                foreach (var heuristic in scoringHeuristics)
                {
                    // Work on copies so each strategy starts fresh - the layout
                    // mutates Width/Height when rotating, which would poison later
                    // strategies if we shared instances.
                    var copies = parts.Select(ClonePartForLayout).ToList();
                    var sorted = copies.OrderBy(sortKey).ToList();
                    var attempt = PackMaxRects(sorted, plateWidth, plateHeight, spacing, heuristic);

                    if (IsBetterResult(attempt, bestResult))
                    {
                        bestResult = attempt;
                    }
                }
            }

            // Propagate the winning placement back onto the caller's part instances
            // so downstream code (geometry cloning, labeling, etc.) sees the chosen
            // positions and rotation flags.
            ApplyResultToOriginals(parts, bestResult);

            return bestResult;
        }

        /// <summary>Scoring heuristic for picking the best free-rect to place a part in.</summary>
        private enum FitHeuristic
        {
            BestShortSide, // minimize the smaller leftover dimension - tightest packing
            BestLongSide,  // minimize the larger leftover dimension - balanced packing
            BestArea       // minimize remaining area in the chosen rect
        }

        /// <summary>
        /// One pass of the MAXRECTS bin-packing algorithm. Walks the parts in the
        /// given order, picks the best-fitting free rectangle (with optional 90°
        /// rotation), splits all overlapping free rects, and prunes contained ones.
        /// </summary>
        private static NestingResult PackMaxRects(
            List<NestingPart> parts,
            double plateWidth,
            double plateHeight,
            double spacing,
            FitHeuristic heuristic)
        {
            var result = new NestingResult { PlateWidth = plateWidth, PlateHeight = plateHeight };

            // The starting free rectangle is the full plate inset by spacing on
            // every side, guaranteeing no part lands on the plate edge.
            var freeRects = new List<FreeRect>
            {
                new FreeRect(spacing, spacing, plateWidth - 2 * spacing, plateHeight - 2 * spacing)
            };

            double totalUsedArea = 0;

            foreach (var part in parts)
            {
                int bestIndex = -1;
                double bestPrimaryScore = double.MaxValue;
                double bestSecondaryScore = double.MaxValue;
                bool bestRotated = false;

                // Try fitting in every free rect, both rotations
                for (int i = 0; i < freeRects.Count; i++)
                {
                    var rect = freeRects[i];

                    // Non-rotated fit
                    if (part.Width <= rect.Width + 1e-9 && part.Height <= rect.Height + 1e-9)
                    {
                        var (primary, secondary) = ScoreFit(part.Width, part.Height, rect, heuristic);
                        if (primary < bestPrimaryScore ||
                            (Math.Abs(primary - bestPrimaryScore) < 1e-9 && secondary < bestSecondaryScore))
                        {
                            bestPrimaryScore = primary;
                            bestSecondaryScore = secondary;
                            bestIndex = i;
                            bestRotated = false;
                        }
                    }

                    // Rotated 90° fit
                    if (part.Height <= rect.Width + 1e-9 && part.Width <= rect.Height + 1e-9)
                    {
                        var (primary, secondary) = ScoreFit(part.Height, part.Width, rect, heuristic);
                        if (primary < bestPrimaryScore ||
                            (Math.Abs(primary - bestPrimaryScore) < 1e-9 && secondary < bestSecondaryScore))
                        {
                            bestPrimaryScore = primary;
                            bestSecondaryScore = secondary;
                            bestIndex = i;
                            bestRotated = true;
                        }
                    }
                }

                if (bestIndex < 0)
                {
                    // No free rect fits this part in either orientation
                    result.UnplacedParts.Add(part);
                    continue;
                }

                var bestRect = freeRects[bestIndex];
                double placedW = bestRotated ? part.Height : part.Width;
                double placedH = bestRotated ? part.Width : part.Height;

                part.NestedX = bestRect.X;
                part.NestedY = bestRect.Y;
                part.IsPlaced = true;

                if (bestRotated)
                {
                    part.IsRotated = true;
                    var t = part.Width;
                    part.Width = part.Height;
                    part.Height = t;
                }

                result.PlacedParts.Add(part);
                totalUsedArea += part.Width * part.Height;

                // Forbidden zone for future parts: placed footprint + spacing buffer
                double forbidLeft = part.NestedX - spacing;
                double forbidRight = part.NestedX + placedW + spacing;
                double forbidBottom = part.NestedY - spacing;
                double forbidTop = part.NestedY + placedH + spacing;

                // Split EVERY free rect that overlaps the forbidden zone into up to
                // 4 sub-rects (left/right/bottom/top of the placed part). This is
                // what differentiates MAXRECTS from guillotine splitting - we keep
                // overlapping maximal sub-rects rather than committing to one cut.
                var newFreeRects = new List<FreeRect>();
                foreach (var fr in freeRects)
                {
                    bool overlaps =
                        fr.X < forbidRight &&
                        fr.X + fr.Width > forbidLeft &&
                        fr.Y < forbidTop &&
                        fr.Y + fr.Height > forbidBottom;

                    if (!overlaps)
                    {
                        newFreeRects.Add(fr);
                        continue;
                    }

                    // Left strip (room to the left of placed part within fr)
                    if (forbidLeft > fr.X)
                    {
                        newFreeRects.Add(new FreeRect(
                            fr.X, fr.Y,
                            forbidLeft - fr.X,
                            fr.Height));
                    }
                    // Right strip
                    if (forbidRight < fr.X + fr.Width)
                    {
                        newFreeRects.Add(new FreeRect(
                            forbidRight, fr.Y,
                            fr.X + fr.Width - forbidRight,
                            fr.Height));
                    }
                    // Bottom strip
                    if (forbidBottom > fr.Y)
                    {
                        newFreeRects.Add(new FreeRect(
                            fr.X, fr.Y,
                            fr.Width,
                            forbidBottom - fr.Y));
                    }
                    // Top strip
                    if (forbidTop < fr.Y + fr.Height)
                    {
                        newFreeRects.Add(new FreeRect(
                            fr.X, forbidTop,
                            fr.Width,
                            fr.Y + fr.Height - forbidTop));
                    }
                }

                freeRects = PruneFreeRects(newFreeRects);
            }

            result.UsedArea = totalUsedArea;
            result.Efficiency = (totalUsedArea / (plateWidth * plateHeight)) * 100;
            return result;
        }

        /// <summary>
        /// Score a candidate placement: lower is better. Returns (primary, secondary)
        /// where primary is the heuristic's main score and secondary is a tiebreaker.
        /// </summary>
        private static (double primary, double secondary) ScoreFit(
            double partW, double partH, FreeRect rect, FitHeuristic heuristic)
        {
            double leftoverW = rect.Width - partW;
            double leftoverH = rect.Height - partH;
            double shortLeftover = Math.Min(leftoverW, leftoverH);
            double longLeftover = Math.Max(leftoverW, leftoverH);

            switch (heuristic)
            {
                case FitHeuristic.BestShortSide:
                    return (shortLeftover, longLeftover);
                case FitHeuristic.BestLongSide:
                    return (longLeftover, shortLeftover);
                case FitHeuristic.BestArea:
                    return (rect.Width * rect.Height - partW * partH, shortLeftover);
                default:
                    return (shortLeftover, longLeftover);
            }
        }

        /// <summary>
        /// Remove free rectangles that are fully contained within other free rects,
        /// and any with degenerate (zero/negative) dimensions. The MAXRECTS split
        /// step routinely produces redundant overlapping rects; pruning them keeps
        /// the candidate list small and prevents the BSSF score from being fooled
        /// by a tiny sliver inside a larger rect.
        /// </summary>
        private static List<FreeRect> PruneFreeRects(List<FreeRect> rects)
        {
            // First pass: drop rects with non-positive dimensions
            var alive = rects.Where(r => r.Width > 1e-6 && r.Height > 1e-6).ToList();

            // Second pass: drop any rect contained within another
            var keep = new List<FreeRect>();
            for (int i = 0; i < alive.Count; i++)
            {
                bool contained = false;
                for (int j = 0; j < alive.Count; j++)
                {
                    if (i == j) continue;
                    if (IsContained(alive[i], alive[j]))
                    {
                        contained = true;
                        break;
                    }
                }
                if (!contained) keep.Add(alive[i]);
            }
            return keep;
        }

        private static bool IsContained(FreeRect inner, FreeRect outer)
        {
            return inner.X >= outer.X - 1e-9 &&
                   inner.Y >= outer.Y - 1e-9 &&
                   inner.X + inner.Width <= outer.X + outer.Width + 1e-9 &&
                   inner.Y + inner.Height <= outer.Y + outer.Height + 1e-9;
        }

        /// <summary>
        /// Compare a new packing attempt against the current best.
        /// Better = more parts placed, then higher area utilization as tiebreaker.
        /// </summary>
        private static bool IsBetterResult(NestingResult attempt, NestingResult current)
        {
            if (current == null) return true;
            if (attempt.PlacedParts.Count > current.PlacedParts.Count) return true;
            if (attempt.PlacedParts.Count < current.PlacedParts.Count) return false;
            return attempt.UsedArea > current.UsedArea;
        }

        /// <summary>
        /// Create a shallow copy of a NestingPart suitable for layout trials.
        /// Width/Height/IsPlaced/IsRotated/NestedX/NestedY are reset to a clean state;
        /// everything else (geometry bytes, source id, lay-flat rotation) is shared
        /// since those don't change during layout.
        /// </summary>
        private static NestingPart ClonePartForLayout(NestingPart p)
        {
            return new NestingPart
            {
                SourceObjectId = p.SourceObjectId,
                PartName = p.PartName,
                HasMetadata = p.HasMetadata,
                Width = p.Width,
                Height = p.Height,
                Thickness = p.Thickness,
                OriginalMin = p.OriginalMin,
                OriginalMax = p.OriginalMax,
                LayFlatRotation = p.LayFlatRotation,
                GeometryDwgBytes = p.GeometryDwgBytes
                // NestedX/Y, IsRotated, IsPlaced default to 0/false
            };
        }

        /// <summary>
        /// Push the placement decisions from <paramref name="best"/> (which holds
        /// copies) back onto the caller's original NestingPart instances, then
        /// rebuild best.PlacedParts/UnplacedParts to reference the originals so
        /// downstream code sees a consistent object identity.
        /// </summary>
        private static void ApplyResultToOriginals(List<NestingPart> originals, NestingResult best)
        {
            // Reset all originals first
            foreach (var p in originals)
            {
                p.NestedX = 0;
                p.NestedY = 0;
                p.IsPlaced = false;
                p.IsRotated = false;
            }

            // Build a lookup from SourceObjectId to original
            var byId = new Dictionary<ObjectId, NestingPart>();
            foreach (var p in originals)
            {
                if (!byId.ContainsKey(p.SourceObjectId))
                {
                    byId[p.SourceObjectId] = p;
                }
            }

            var newPlaced = new List<NestingPart>();
            foreach (var copy in best.PlacedParts)
            {
                if (byId.TryGetValue(copy.SourceObjectId, out var orig))
                {
                    orig.NestedX = copy.NestedX;
                    orig.NestedY = copy.NestedY;
                    orig.Width = copy.Width;     // may be swapped vs. input
                    orig.Height = copy.Height;   // may be swapped vs. input
                    orig.IsRotated = copy.IsRotated;
                    orig.IsPlaced = true;
                    newPlaced.Add(orig);
                }
            }

            var newUnplaced = new List<NestingPart>();
            foreach (var copy in best.UnplacedParts)
            {
                if (byId.TryGetValue(copy.SourceObjectId, out var orig))
                {
                    newUnplaced.Add(orig);
                }
            }

            best.PlacedParts = newPlaced;
            best.UnplacedParts = newUnplaced;
        }

        /// <summary>
        /// Create a new drawing populated with the nested geometry.
        /// Backwards-compatible overload: starts from a blank in-memory
        /// document. Equivalent to calling
        /// CreateNestingDrawing(result, null, null, null).
        /// </summary>
        public static string CreateNestingDrawing(NestingResult nestingResult)
        {
            return CreateNestingDrawing(nestingResult, null, null, null);
        }

        /// <summary>
        /// Create a new drawing populated with the nested geometry, optionally
        /// based on a title-block template.
        ///
        /// When <paramref name="templatePath"/> and <paramref name="outputPath"/>
        /// are both provided, the template is copied to the output path on
        /// disk (so we don't fight ReadDwgFile's locking on a .dwg template),
        /// optional MC drawing properties are stamped into that copy, the
        /// file is opened as the active document, the nest geometry is
        /// written into modelspace with the plate's lower-left at (0,0,0),
        /// and a QSAVE is queued so the file ends up with both the title
        /// block and the nest persisted.
        ///
        /// When either path is null, falls back to the original in-memory
        /// flow: a brand new document is added and the nest geometry is
        /// drawn into it. The user can save it manually.
        /// </summary>
        public static string CreateNestingDrawing(
            NestingResult nestingResult,
            string templatePath,
            string outputPath,
            DrawingPropertiesData propertiesToStamp)
        {
            var docMgr = AcadApp.DocumentManager;

            bool fromTemplate =
                !string.IsNullOrEmpty(templatePath) &&
                File.Exists(templatePath) &&
                !string.IsNullOrEmpty(outputPath);

            Document newDoc;

            if (fromTemplate)
            {
                // Step 1: copy the title-block template to the target path.
                // File.Copy is more permissive than ReadDwgFile, so it
                // doesn't trip eFileSharingViolation when the template is a
                // .dwg.
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.Copy(templatePath, outputPath, overwrite: false);

                // Step 2: re-stamp MC drawing properties so the new file
                // identifies as a Sheet rather than carrying the template's
                // identity. Done via a side-database before opening, since
                // we can't SaveAs the active document to its own path.
                if (propertiesToStamp != null)
                {
                    using (var sideDb = new Database(false, true))
                    {
                        sideDb.ReadDwgFile(outputPath, FileShare.ReadWrite, true, "");
                        sideDb.CloseInput(true);
                        DrawingPropertiesManager.Write(sideDb, propertiesToStamp);
                        sideDb.SaveAs(outputPath, DwgVersion.Current);
                    }
                }

                // Step 3: open the file as the active document.
                newDoc = docMgr.Open(outputPath, false);
            }
            else
            {
                // Fallback: blank in-memory drawing.
                newDoc = docMgr.Add("");
            }

            docMgr.MdiActiveDocument = newDoc;

            using (DocumentLock docLock = newDoc.LockDocument())
            {
                WriteNestGeometryToModelSpace(newDoc.Database, nestingResult);

                // Force top-down plan view so the nest reads as a 2D layout
                // even when source parts are 3D solids. Without this, the
                // viewer sees the parts in iso/3D perspective and mistakes
                // the slab thickness for parts standing on their edges.
                try
                {
                    SetTopDownView(newDoc.Database);
                }
                catch { }

                // Show the new geometry. Wrapped because Editor.Command can
                // fail in odd contexts and the rest of the work is already
                // committed.
                try
                {
                    newDoc.Editor.Regen();
                    newDoc.Editor.Command("_.ZOOM", "_E");
                }
                catch { }
            }

            // Persist when we're saving to a real path. SendStringToExecute
            // is queued; the QSAVE runs after the calling command exits.
            // (Database.SaveAs to the active document's own path throws
            // eNotApplicable, so QSAVE is the right tool here.)
            if (fromTemplate)
            {
                try { newDoc.SendStringToExecute("_.QSAVE\n", true, false, false); }
                catch { }
            }

            return newDoc.Name;
        }

        /// <summary>
        /// Render a NestingResult into the given database's modelspace.
        /// Plate's lower-left sits at (0,0,0). For each placed part the
        /// real source geometry (curves, holes, ACIS, ...) is wblock-cloned
        /// from its captured .dwg blob and transformed into the layout
        /// position; if cloning fails for any reason we draw a bounding
        /// rectangle so the plate is still complete.
        ///
        /// Labels (part name + dimensions) are only added when the part
        /// carried MC metadata. Parts selected without metadata are placed
        /// silently - that's the "just arrange and place" workflow for raw
        /// 2D shapes.
        /// </summary>
        private static void WriteNestGeometryToModelSpace(Database db, NestingResult nestingResult)
        {
            // Step 0: ensure the layers exist before clones land on them.
            // Plate uses color 195 (cyan-blue). Parts use color 1 (red).
            // Labels use color 7 (white/black depending on background).
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                CreateLayerIfNotExists(lt, tr, "MC_PLATE", 195);
                CreateLayerIfNotExists(lt, tr, "MC_PARTS", 1);
                CreateLayerIfNotExists(lt, tr, "MC_LABELS", 7);
                tr.Commit();
            }

            // Step 1: clone each part's actual geometry into modelspace.
            // CloneGeometryIntoDb opens its own internal transaction over
            // the target db, so this MUST run outside any other transaction
            // we're holding here. Track which parts succeeded so step 2
            // knows whether it needs to draw a fallback bounding rectangle.
            var clonedParts = new HashSet<NestingPart>();
            foreach (var part in nestingResult.PlacedParts)
            {
                if (part.GeometryDwgBytes == null || part.GeometryDwgBytes.Length == 0)
                {
                    // No geometry data available - will draw fallback rectangle
                    continue;
                }

                try
                {
                    var xform = ComputeNestPartTransform(part);
                    var clones = PartGeometryHelper.CloneGeometryIntoDb(
                        part.GeometryDwgBytes, db, xform, "MC_PARTS", 1);

                    // Only mark as cloned if we actually got entities back.
                    // If clones list is empty, we'll fall back to drawing a
                    // rectangle, which means the serialization or cloning failed.
                    if (clones != null && clones.Count > 0)
                    {
                        clonedParts.Add(part);
                    }
                    // If clones is null or empty, the part stays out of
                    // clonedParts and we'll draw a fallback in step 2.
                }
                catch (System.Exception ex)
                {
                    // Cloning failed - log it for debugging and fall through
                    // to fallback rectangle drawing. We intentionally don't
                    // rethrow here to allow the nest to complete partially.
                    System.Diagnostics.Debug.WriteLine(
                        $"Failed to clone nested part geometry: {ex.Message}");
                }
            }

            // Step 2: plate, fallback rects, labels, summary - one transaction.
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var ms = (BlockTableRecord)tr.GetObject(modelSpaceId, OpenMode.ForWrite);

                DrawPlateOutline(ms, tr, db, nestingResult);

                foreach (var part in nestingResult.PlacedParts)
                {
                    if (!clonedParts.Contains(part))
                    {
                        DrawFallbackRectangle(ms, tr, db, part);
                    }

                    // Labels: only when the part carries MC metadata. Parts
                    // selected raw (no XData) are placed silently per the
                    // "just arrange and place" workflow.
                    if (part.HasMetadata && !string.IsNullOrEmpty(part.PartName))
                    {
                        DrawPartLabels(ms, tr, db, part);
                    }
                }

                DrawSummaryText(ms, tr, db, nestingResult);

                tr.Commit();
            }
        }

        /// <summary>
        /// Build the complete transform matrix for placing a part in the nest.
        /// Strategy: combine all rotations (lay-flat + layout), apply them to
        /// the original 8 bbox corners to find the post-rotation bbox min,
        /// then translate so that new min lands at (NestedX, NestedY, 0).
        ///
        /// This is more robust than chaining rotations and translates separately
        /// because the rotated bbox may shift in unpredictable ways depending
        /// on the rotation axis and original bbox position.
        /// </summary>
        private static Matrix3d ComputeNestPartTransform(NestingPart part)
        {
            var origMin = part.OriginalMin;
            var origMax = part.OriginalMax;

            // Step 1: combine lay-flat rotation and (optional) layout rotation.
            // Both pivot at origMin so origMin stays put through the combined rotation.
            Matrix3d combinedRotation = part.LayFlatRotation;
            if (part.IsRotated)
            {
                var layoutRot = Matrix3d.Rotation(Math.PI / 2, Vector3d.ZAxis, origMin);
                combinedRotation = layoutRot * combinedRotation;
            }

            // Step 2: apply the combined rotation to all 8 corners of the
            // original axis-aligned bbox to determine the new bbox-min after
            // rotation. This handles any axis shift the rotations introduce.
            var corners = new[]
            {
                new Point3d(origMin.X, origMin.Y, origMin.Z),
                new Point3d(origMax.X, origMin.Y, origMin.Z),
                new Point3d(origMin.X, origMax.Y, origMin.Z),
                new Point3d(origMax.X, origMax.Y, origMin.Z),
                new Point3d(origMin.X, origMin.Y, origMax.Z),
                new Point3d(origMax.X, origMin.Y, origMax.Z),
                new Point3d(origMin.X, origMax.Y, origMax.Z),
                new Point3d(origMax.X, origMax.Y, origMax.Z)
            };

            double newMinX = double.MaxValue;
            double newMinY = double.MaxValue;
            double newMinZ = double.MaxValue;
            foreach (var c in corners)
            {
                var r = c.TransformBy(combinedRotation);
                if (r.X < newMinX) newMinX = r.X;
                if (r.Y < newMinY) newMinY = r.Y;
                if (r.Z < newMinZ) newMinZ = r.Z;
            }

            // Step 3: translate so the new bbox min lands at (NestedX, NestedY, 0).
            // This guarantees the part fits within its layout cell starting
            // at the lower-left, regardless of how the rotations shifted it.
            var translate = Matrix3d.Displacement(new Vector3d(
                part.NestedX - newMinX,
                part.NestedY - newMinY,
                -newMinZ));

            return translate * combinedRotation;
        }

        private static void DrawPlateOutline(BlockTableRecord ms, Transaction tr, Database db, NestingResult result)
        {
            var plate = new Polyline();
            plate.SetDatabaseDefaults(db);
            plate.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
            plate.AddVertexAt(1, new Point2d(result.PlateWidth, 0), 0, 0, 0);
            plate.AddVertexAt(2, new Point2d(result.PlateWidth, result.PlateHeight), 0, 0, 0);
            plate.AddVertexAt(3, new Point2d(0, result.PlateHeight), 0, 0, 0);
            plate.Closed = true;
            plate.Layer = "MC_PLATE";
            plate.ColorIndex = 195;
            ms.AppendEntity(plate);
            tr.AddNewlyCreatedDBObject(plate, true);
        }

        private static void DrawFallbackRectangle(BlockTableRecord ms, Transaction tr, Database db, NestingPart part)
        {
            var rect = new Polyline();
            rect.SetDatabaseDefaults(db);
            rect.AddVertexAt(0, new Point2d(part.NestedX, part.NestedY), 0, 0, 0);
            rect.AddVertexAt(1, new Point2d(part.NestedX + part.Width, part.NestedY), 0, 0, 0);
            rect.AddVertexAt(2, new Point2d(part.NestedX + part.Width, part.NestedY + part.Height), 0, 0, 0);
            rect.AddVertexAt(3, new Point2d(part.NestedX, part.NestedY + part.Height), 0, 0, 0);
            rect.Closed = true;
            rect.Layer = "MC_PARTS";
            rect.ColorIndex = 1;
            ms.AppendEntity(rect);
            tr.AddNewlyCreatedDBObject(rect, true);
        }

        private static void DrawPartLabels(BlockTableRecord ms, Transaction tr, Database db, NestingPart part)
        {
            double labelX = part.NestedX + part.Width / 2;
            double labelY = part.NestedY + part.Height / 2;
            double textHeight = Math.Max(5, Math.Min(20, Math.Min(part.Width, part.Height) / 8));

            var nameText = new DBText();
            nameText.SetDatabaseDefaults(db);
            nameText.TextString = part.PartName;
            nameText.Height = textHeight;
            nameText.Layer = "MC_LABELS";
            nameText.ColorIndex = 7;
            nameText.Position = new Point3d(labelX, labelY, 0);
            nameText.HorizontalMode = TextHorizontalMode.TextCenter;
            nameText.VerticalMode = TextVerticalMode.TextVerticalMid;
            nameText.AlignmentPoint = new Point3d(labelX, labelY, 0);
            ms.AppendEntity(nameText);
            tr.AddNewlyCreatedDBObject(nameText, true);

            var dimText = new DBText();
            dimText.SetDatabaseDefaults(db);
            dimText.TextString = $"{part.Width:F0}x{part.Height:F0}";
            dimText.Height = textHeight * 0.5;
            dimText.Layer = "MC_LABELS";
            dimText.ColorIndex = 7;
            dimText.Position = new Point3d(labelX, labelY - textHeight, 0);
            dimText.HorizontalMode = TextHorizontalMode.TextCenter;
            dimText.VerticalMode = TextVerticalMode.TextVerticalMid;
            dimText.AlignmentPoint = new Point3d(labelX, labelY - textHeight, 0);
            ms.AppendEntity(dimText);
            tr.AddNewlyCreatedDBObject(dimText, true);
        }

        private static void DrawSummaryText(BlockTableRecord ms, Transaction tr, Database db, NestingResult result)
        {
            var info = new DBText();
            info.SetDatabaseDefaults(db);
            info.TextString = $"Plate: {result.PlateWidth} x {result.PlateHeight} mm  |  Parts: {result.PlacedParts.Count}  |  Efficiency: {result.Efficiency:F1}%";
            info.Height = 20;
            info.Layer = "MC_LABELS";
            info.ColorIndex = 7;
            info.Position = new Point3d(0, result.PlateHeight + 30, 0);
            ms.AppendEntity(info);
            tr.AddNewlyCreatedDBObject(info, true);
        }

        /// <summary>
        /// Set the active viewport to a top-down (plan) view of the WCS XY plane.
        /// This is critical for nest output - parts cloned from 3D solids would
        /// otherwise show their thickness as visible depth in iso views,
        /// making the layout look wrong.
        /// </summary>
        private static void SetTopDownView(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var vt = (ViewportTable)tr.GetObject(db.ViewportTableId, OpenMode.ForRead);
                if (vt.Has("*Active"))
                {
                    var vtr = (ViewportTableRecord)tr.GetObject(vt["*Active"], OpenMode.ForWrite);
                    // Look straight down the Z axis at the XY plane
                    vtr.ViewDirection = new Vector3d(0, 0, 1);
                    vtr.ViewTwist = 0;
                }
                tr.Commit();
            }
        }

        private static void CreateLayerIfNotExists(LayerTable lt, Transaction tr, string name, short colorIndex)
        {
            if (!lt.Has(name))
            {
                var layer = new LayerTableRecord();
                layer.Name = name;
                layer.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                lt.Add(layer);
                tr.AddNewlyCreatedDBObject(layer, true);
            }
        }
    }

    public class FreeRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public FreeRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}
