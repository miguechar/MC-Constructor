using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FirstAcadPlugin
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

                    parts.Add(new NestingPart
                    {
                        SourceObjectId = selObj.ObjectId,
                        PartName = partName,
                        HasMetadata = hasMetadata,
                        Width = width,
                        Height = depth,
                        Thickness = height,
                        OriginalMin = ext.MinPoint,
                        OriginalMax = ext.MaxPoint
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

        public static NestingResult NestPartsGuillotine(List<NestingPart> parts, double plateWidth, double plateHeight, double spacing = DEFAULT_SPACING)
        {
            var result = new NestingResult
            {
                PlateWidth = plateWidth,
                PlateHeight = plateHeight
            };

            var freeRects = new List<FreeRect>
            {
                new FreeRect(spacing, spacing, plateWidth - 2 * spacing, plateHeight - 2 * spacing)
            };

            var sortedParts = parts.OrderByDescending(p => p.Width * p.Height).ToList();
            double totalUsedArea = 0;

            foreach (var part in sortedParts)
            {
                int bestIndex = -1;
                double bestShortSide = double.MaxValue;
                bool bestRotated = false;

                for (int i = 0; i < freeRects.Count; i++)
                {
                    var rect = freeRects[i];

                    if (part.Width <= rect.Width && part.Height <= rect.Height)
                    {
                        double shortSide = Math.Min(rect.Width - part.Width, rect.Height - part.Height);
                        if (shortSide < bestShortSide)
                        {
                            bestShortSide = shortSide;
                            bestIndex = i;
                            bestRotated = false;
                        }
                    }

                    if (part.Height <= rect.Width && part.Width <= rect.Height)
                    {
                        double shortSide = Math.Min(rect.Width - part.Height, rect.Height - part.Width);
                        if (shortSide < bestShortSide)
                        {
                            bestShortSide = shortSide;
                            bestIndex = i;
                            bestRotated = true;
                        }
                    }
                }

                if (bestIndex >= 0)
                {
                    var rect = freeRects[bestIndex];

                    double placeWidth = bestRotated ? part.Height : part.Width;
                    double placeHeight = bestRotated ? part.Width : part.Height;

                    part.NestedX = rect.X;
                    part.NestedY = rect.Y;

                    if (bestRotated)
                    {
                        // Track the rotation so the geometry transform in
                        // the output step can apply a 90deg CCW rotation,
                        // not just a translation.
                        part.IsRotated = true;
                        double temp = part.Width;
                        part.Width = part.Height;
                        part.Height = temp;
                    }

                    part.IsPlaced = true;
                    result.PlacedParts.Add(part);
                    totalUsedArea += part.Width * part.Height;

                    freeRects.RemoveAt(bestIndex);

                    if (rect.Width - placeWidth - spacing > spacing)
                    {
                        freeRects.Add(new FreeRect(
                            rect.X + placeWidth + spacing,
                            rect.Y,
                            rect.Width - placeWidth - spacing,
                            rect.Height
                        ));
                    }

                    if (rect.Height - placeHeight - spacing > spacing)
                    {
                        freeRects.Add(new FreeRect(
                            rect.X,
                            rect.Y + placeHeight + spacing,
                            placeWidth,
                            rect.Height - placeHeight - spacing
                        ));
                    }
                }
                else
                {
                    result.UnplacedParts.Add(part);
                }
            }

            result.UsedArea = totalUsedArea;
            result.Efficiency = (totalUsedArea / (plateWidth * plateHeight)) * 100;

            return result;
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
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                CreateLayerIfNotExists(lt, tr, "MC_PLATE", 1);
                CreateLayerIfNotExists(lt, tr, "MC_PARTS", 3);
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
                        part.GeometryDwgBytes, db, xform, "MC_PARTS", 3);

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
        /// Build the matrix that maps the source entity's bbox-min onto
        /// (NestedX, NestedY, 0). When IsRotated is set, the geometry is
        /// rotated 90deg CCW around its source bbox-min before the
        /// final translate so the rotated bbox-min ends up at the layout
        /// position.
        /// </summary>
        private static Matrix3d ComputeNestPartTransform(NestingPart part)
        {
            var origMin = part.OriginalMin;

            if (part.IsRotated)
            {
                // Rotation around origMin maps the original bbox
                // [Xmin, Ymin] -> [Xmax, Ymax] to a new bbox whose
                // lower-left is at (Xmin - origH, Ymin), where origH is
                // the *original* Y-extent. We translate that new lower-left
                // to (NestedX, NestedY) and bring Z to zero.
                double origH = part.OriginalMax.Y - part.OriginalMin.Y;

                var rot = Matrix3d.Rotation(Math.PI / 2, Vector3d.ZAxis, origMin);
                var translate = Matrix3d.Displacement(new Vector3d(
                    part.NestedX + origH - origMin.X,
                    part.NestedY - origMin.Y,
                    -origMin.Z));

                // Right-to-left composition: rotate first, then translate.
                return translate * rot;
            }

            return Matrix3d.Displacement(new Vector3d(
                part.NestedX - origMin.X,
                part.NestedY - origMin.Y,
                -origMin.Z));
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
            plate.ColorIndex = 1;
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
            rect.ColorIndex = 3;
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
