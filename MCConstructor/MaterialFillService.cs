using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MCConstructor
{
    public class MaterialFillOptions
    {
        public MaterialPlate Plate { get; set; }
        public double Gap { get; set; }
        public bool AllowRotate { get; set; }
        public MaterialSeamGuides SeamGuides { get; set; } = new MaterialSeamGuides();
    }

    public class MaterialSeamGuides
    {
        public List<double> PossibleVertical { get; } = new List<double>();
        public List<double> PossibleHorizontal { get; } = new List<double>();
        public List<double> BlockedVertical { get; } = new List<double>();
        public List<double> BlockedHorizontal { get; } = new List<double>();

        public int PossibleCount => PossibleVertical.Count + PossibleHorizontal.Count;
        public int BlockedCount => BlockedVertical.Count + BlockedHorizontal.Count;
        public bool HasAny => PossibleCount > 0 || BlockedCount > 0;
    }

    public class MaterialFillResult
    {
        public int BoundaryCount { get; set; }
        public int SheetCount { get; set; }
        public int FullSheets { get; set; }
        public int TrimmedSheets { get; set; }
        public int ReusedTrimmedPieces { get; set; }
        public int OffcutCount { get; set; }
        public int PossibleSeamGuideCount { get; set; }
        public int BlockedSeamGuideCount { get; set; }
        public double BoundaryArea { get; set; }
        public double PlacedArea { get; set; }
        public double WasteArea { get; set; }
        public int SkippedBoundaries { get; set; }
    }

    public class MaterialFillBoundary
    {
        public ObjectId ObjectId { get; set; }
        public List<Point2d> Vertices { get; set; }
    }

    internal class FillPiece
    {
        public int BoundaryIndex { get; set; }
        public int BoundaryPieceIndex { get; set; }
        public List<Point2d> Polygon { get; set; }
        public double Area { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsFullSheet { get; set; }
        public string SheetLabel { get; set; }
        public string PieceLabel { get; set; }
        public bool RotatedForCut { get; set; }
    }

    internal class FillStockSheet
    {
        public int SheetNumber { get; set; }
        public string Label { get; set; }
        public List<FillPiece> Pieces { get; } = new List<FillPiece>();
        public List<FillFreeRect> FreeRects { get; } = new List<FillFreeRect>();
    }

    internal class FillFreeRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public FillStockSheet Sheet { get; set; }

        public FillFreeRect(double x, double y, double width, double height, FillStockSheet sheet)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Sheet = sheet;
        }
    }

    internal class FillAxisRun
    {
        public double Start { get; set; }
        public double End { get; set; }
        public double Length => End - Start;
    }

    public static class MaterialFillService
    {
        private const double Tolerance = 1e-6;

        public static MaterialFillResult FillBoundaries(
            Database db,
            IEnumerable<MaterialFillBoundary> boundaries,
            MaterialFillOptions options)
        {
            var result = new MaterialFillResult();
            result.PossibleSeamGuideCount = options.SeamGuides?.PossibleCount ?? 0;
            result.BlockedSeamGuideCount = options.SeamGuides?.BlockedCount ?? 0;
            var validBoundaries = new List<List<Point2d>>();

            foreach (var boundary in boundaries)
            {
                if (boundary.Vertices == null || boundary.Vertices.Count < 3 || !IsConvex(boundary.Vertices))
                {
                    result.SkippedBoundaries++;
                    continue;
                }

                validBoundaries.Add(boundary.Vertices);
                result.BoundaryCount++;
                result.BoundaryArea += Math.Abs(SignedArea(boundary.Vertices));
            }

            var pieces = CreateRequiredPieces(validBoundaries, options, result);
            var trimSheets = AssignPiecesToStockSheets(pieces, options, result);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayers(db, tr);

                ObjectId modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var ms = (BlockTableRecord)tr.GetObject(modelSpaceId, OpenMode.ForWrite);

                foreach (var piece in pieces)
                    DrawWallPiece(ms, tr, db, piece);

                DrawBoundarySummaries(ms, tr, db, validBoundaries, pieces, options.Plate);
                DrawRemainingOffcuts(ms, tr, db, validBoundaries, trimSheets, options, result);

                tr.Commit();
            }

            result.WasteArea = Math.Max(0, result.SheetCount * options.Plate.Width * options.Plate.Height - result.PlacedArea);
            return result;
        }

        public static List<MaterialFillBoundary> ExtractPolylineBoundaries(
            Database db,
            SelectionSet selection,
            out int skipped)
        {
            var boundaries = new List<MaterialFillBoundary>();
            skipped = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selected in selection)
                {
                    var polyline = tr.GetObject(selected.ObjectId, OpenMode.ForRead) as Polyline;
                    if (polyline == null || !polyline.Closed || polyline.NumberOfVertices < 3)
                    {
                        skipped++;
                        continue;
                    }

                    var vertices = new List<Point2d>();
                    bool hasCurves = false;
                    for (int i = 0; i < polyline.NumberOfVertices; i++)
                    {
                        if (Math.Abs(polyline.GetBulgeAt(i)) > Tolerance)
                        {
                            hasCurves = true;
                            break;
                        }
                        vertices.Add(polyline.GetPoint2dAt(i));
                    }

                    if (hasCurves || Math.Abs(SignedArea(vertices)) < Tolerance)
                    {
                        skipped++;
                        continue;
                    }

                    if (SignedArea(vertices) < 0)
                        vertices.Reverse();

                    boundaries.Add(new MaterialFillBoundary
                    {
                        ObjectId = selected.ObjectId,
                        Vertices = vertices
                    });
                }

                tr.Commit();
            }

            return boundaries;
        }

        public static MaterialSeamGuides ExtractSeamGuides(
            Database db,
            SelectionSet possibleSelection,
            SelectionSet blockedSelection)
        {
            var guides = new MaterialSeamGuides();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                AddSeamGuidesFromSelection(tr, possibleSelection, guides.PossibleVertical, guides.PossibleHorizontal);
                AddSeamGuidesFromSelection(tr, blockedSelection, guides.BlockedVertical, guides.BlockedHorizontal);
                tr.Commit();
            }

            SortAndDeduplicate(guides.PossibleVertical);
            SortAndDeduplicate(guides.PossibleHorizontal);
            SortAndDeduplicate(guides.BlockedVertical);
            SortAndDeduplicate(guides.BlockedHorizontal);
            return guides;
        }

        private static void AddSeamGuidesFromSelection(
            Transaction tr,
            SelectionSet selection,
            List<double> vertical,
            List<double> horizontal)
        {
            if (selection == null) return;

            foreach (SelectedObject selected in selection)
            {
                if (selected == null) continue;

                var entity = tr.GetObject(selected.ObjectId, OpenMode.ForRead) as Entity;
                if (entity == null) continue;

                Extents3d ext;
                try { ext = entity.GeometricExtents; }
                catch { continue; }

                double dx = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                double dy = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
                if (dy > dx * 4)
                    vertical.Add((ext.MinPoint.X + ext.MaxPoint.X) / 2);
                else if (dx > dy * 4)
                    horizontal.Add((ext.MinPoint.Y + ext.MaxPoint.Y) / 2);
            }
        }

        private static List<FillPiece> CreateRequiredPieces(
            List<List<Point2d>> boundaries,
            MaterialFillOptions options,
            MaterialFillResult result)
        {
            var pieces = new List<FillPiece>();

            for (int boundaryIndex = 0; boundaryIndex < boundaries.Count; boundaryIndex++)
            {
                var boundary = boundaries[boundaryIndex];
                double minX = boundary.Min(p => p.X);
                double maxX = boundary.Max(p => p.X);
                double minY = boundary.Min(p => p.Y);
                double maxY = boundary.Max(p => p.Y);
                double boundaryWidth = maxX - minX;
                double boundaryHeight = maxY - minY;

                double sheetWidth = options.Plate.Width;
                double sheetHeight = options.Plate.Height;
                if (options.AllowRotate && SheetCount(boundaryWidth, boundaryHeight, sheetHeight, sheetWidth, options.Gap) <
                    SheetCount(boundaryWidth, boundaryHeight, sheetWidth, sheetHeight, options.Gap))
                {
                    sheetWidth = options.Plate.Height;
                    sheetHeight = options.Plate.Width;
                }

                double stepX = sheetWidth + options.Gap;
                double stepY = sheetHeight + options.Gap;
                var xRuns = BuildPanelRuns(
                    minX, maxX, sheetWidth, stepX,
                    options.SeamGuides?.PossibleVertical,
                    options.SeamGuides?.BlockedVertical);
                var yRuns = BuildPanelRuns(
                    minY, maxY, sheetHeight, stepY,
                    options.SeamGuides?.PossibleHorizontal,
                    options.SeamGuides?.BlockedHorizontal);
                int localIndex = 1;

                foreach (var yRun in yRuns)
                {
                    foreach (var xRun in xRuns)
                    {
                        var rect = new List<Point2d>
                        {
                            new Point2d(xRun.Start, yRun.Start),
                            new Point2d(xRun.End, yRun.Start),
                            new Point2d(xRun.End, yRun.End),
                            new Point2d(xRun.Start, yRun.End)
                        };

                        var clipped = ClipPolygon(rect, boundary);
                        double area = Math.Abs(SignedArea(clipped));
                        if (clipped.Count < 3 || area < Tolerance)
                            continue;

                        var ext = GetExtents(clipped);
                        bool isFull = Math.Abs(area - options.Plate.Width * options.Plate.Height) <=
                            Math.Max(1.0, options.Plate.Width * options.Plate.Height * 0.0001);

                        pieces.Add(new FillPiece
                        {
                            BoundaryIndex = boundaryIndex,
                            BoundaryPieceIndex = localIndex++,
                            Polygon = clipped,
                            Area = area,
                            Width = ext.maxX - ext.minX,
                            Height = ext.maxY - ext.minY,
                            IsFullSheet = isFull
                        });

                        result.PlacedArea += area;
                    }
                }
            }

            return pieces;
        }

        private static List<FillAxisRun> BuildPanelRuns(
            double min,
            double max,
            double panelLength,
            double defaultStep,
            List<double> possibleSeams,
            List<double> blockedSeams)
        {
            var runs = new List<FillAxisRun>();
            double current = min;

            while (current < max - Tolerance)
            {
                double next = ChooseNextSeam(current, max, panelLength, possibleSeams, blockedSeams);
                if (next <= current + Tolerance)
                    next = Math.Min(max, current + defaultStep);
                if (next > max) next = max;

                runs.Add(new FillAxisRun { Start = current, End = next });
                current = next >= max - Tolerance ? max : next + Math.Max(0, defaultStep - panelLength);
            }

            return runs;
        }

        private static double ChooseNextSeam(
            double current,
            double max,
            double panelLength,
            List<double> possibleSeams,
            List<double> blockedSeams)
        {
            double maxReach = current + panelLength;
            if (maxReach >= max - Tolerance)
                return max;

            if (possibleSeams != null && possibleSeams.Count > 0)
            {
                var possible = possibleSeams
                    .Where(s => s > current + Tolerance && s <= maxReach + Tolerance && !ContainsNear(blockedSeams, s))
                    .OrderByDescending(s => s)
                    .FirstOrDefault();

                if (possible > current + Tolerance)
                    return possible;
            }

            double candidate = maxReach;
            if (!ContainsNear(blockedSeams, candidate))
                return candidate;

            var beforeBlocked = blockedSeams
                .Where(s => s > current + Tolerance && s < candidate + Tolerance)
                .OrderByDescending(s => s)
                .FirstOrDefault();

            if (beforeBlocked > current + Tolerance)
                return Math.Max(current + Tolerance, beforeBlocked - 1.0);

            return candidate;
        }

        private static List<FillStockSheet> AssignPiecesToStockSheets(
            List<FillPiece> pieces,
            MaterialFillOptions options,
            MaterialFillResult result)
        {
            var trimSheets = new List<FillStockSheet>();
            int nextSheetNumber = 1;

            foreach (var piece in pieces.Where(p => p.IsFullSheet))
            {
                piece.SheetLabel = $"{options.Plate.Code}-{nextSheetNumber++}";
                piece.PieceLabel = piece.SheetLabel;
                result.FullSheets++;
            }

            var trimPieces = pieces
                .Where(p => !p.IsFullSheet)
                .OrderByDescending(p => p.Width * p.Height)
                .ThenByDescending(p => Math.Max(p.Width, p.Height))
                .ToList();

            foreach (var piece in trimPieces)
            {
                FillStockSheet targetSheet = null;
                FillFreeRect targetRect = null;
                bool rotated = false;

                FindTrimFit(trimSheets, piece, options.Gap, out targetSheet, out targetRect, out rotated);
                bool reusedExistingSheet = targetSheet != null && targetSheet.Pieces.Count > 0;

                if (targetSheet == null)
                {
                    targetSheet = new FillStockSheet
                    {
                        SheetNumber = nextSheetNumber,
                        Label = $"{options.Plate.Code}-{nextSheetNumber}"
                    };
                    nextSheetNumber++;
                    targetSheet.FreeRects.Add(new FillFreeRect(
                        0, 0, options.Plate.Width, options.Plate.Height, targetSheet));
                    trimSheets.Add(targetSheet);

                    FindTrimFit(new[] { targetSheet }, piece, options.Gap, out targetSheet, out targetRect, out rotated);
                }

                if (targetSheet == null || targetRect == null)
                    continue;

                PlaceTrimPiece(targetSheet, targetRect, piece, rotated, options.Gap);
                piece.SheetLabel = targetSheet.Label;
                piece.PieceLabel = $"{targetSheet.Label}{SuffixForIndex(targetSheet.Pieces.Count)}";
                piece.RotatedForCut = rotated;
                targetSheet.Pieces.Add(piece);

                if (reusedExistingSheet)
                    result.ReusedTrimmedPieces++;
                result.TrimmedSheets++;
            }

            result.SheetCount = result.FullSheets + trimSheets.Count;
            return trimSheets;
        }

        private static void FindTrimFit(
            IEnumerable<FillStockSheet> sheets,
            FillPiece piece,
            double gap,
            out FillStockSheet targetSheet,
            out FillFreeRect targetRect,
            out bool rotated)
        {
            targetSheet = null;
            targetRect = null;
            rotated = false;

            double bestWaste = double.MaxValue;
            foreach (var sheet in sheets)
            {
                foreach (var rect in sheet.FreeRects)
                {
                    TryCandidate(sheet, rect, piece.Width, piece.Height, false, ref bestWaste,
                        ref targetSheet, ref targetRect, ref rotated);
                    TryCandidate(sheet, rect, piece.Height, piece.Width, true, ref bestWaste,
                        ref targetSheet, ref targetRect, ref rotated);
                }
            }
        }

        private static void TryCandidate(
            FillStockSheet sheet,
            FillFreeRect rect,
            double width,
            double height,
            bool isRotated,
            ref double bestWaste,
            ref FillStockSheet targetSheet,
            ref FillFreeRect targetRect,
            ref bool rotated)
        {
            if (width > rect.Width + Tolerance || height > rect.Height + Tolerance)
                return;

            double waste = rect.Width * rect.Height - width * height;
            if (waste >= bestWaste)
                return;

            bestWaste = waste;
            targetSheet = sheet;
            targetRect = rect;
            rotated = isRotated;
        }

        private static void PlaceTrimPiece(
            FillStockSheet sheet,
            FillFreeRect rect,
            FillPiece piece,
            bool rotated,
            double gap)
        {
            sheet.FreeRects.Remove(rect);

            double cutWidth = rotated ? piece.Height : piece.Width;
            double cutHeight = rotated ? piece.Width : piece.Height;
            double rightWidth = rect.Width - cutWidth - gap;
            double topHeight = rect.Height - cutHeight - gap;

            if (rightWidth > Tolerance)
            {
                sheet.FreeRects.Add(new FillFreeRect(
                    rect.X + cutWidth + gap,
                    rect.Y,
                    rightWidth,
                    cutHeight,
                    sheet));
            }

            if (topHeight > Tolerance)
            {
                sheet.FreeRects.Add(new FillFreeRect(
                    rect.X,
                    rect.Y + cutHeight + gap,
                    rect.Width,
                    topHeight,
                    sheet));
            }

            PruneFreeRects(sheet.FreeRects);
        }

        private static void PruneFreeRects(List<FillFreeRect> rects)
        {
            for (int i = rects.Count - 1; i >= 0; i--)
            {
                if (rects[i].Width <= Tolerance || rects[i].Height <= Tolerance)
                    rects.RemoveAt(i);
            }
        }

        private static string SuffixForIndex(int zeroBasedIndex)
        {
            int value = zeroBasedIndex + 1;
            string result = "";
            while (value > 0)
            {
                value--;
                result = (char)('A' + (value % 26)) + result;
                value /= 26;
            }
            return result;
        }

        private static int SheetCount(double boundaryWidth, double boundaryHeight, double sheetWidth, double sheetHeight, double gap)
        {
            double stepX = sheetWidth + gap;
            double stepY = sheetHeight + gap;
            return (int)Math.Ceiling(boundaryWidth / stepX) * (int)Math.Ceiling(boundaryHeight / stepY);
        }

        private static List<Point2d> ClipPolygon(List<Point2d> subject, List<Point2d> clip)
        {
            var output = new List<Point2d>(subject);

            for (int i = 0; i < clip.Count; i++)
            {
                var a = clip[i];
                var b = clip[(i + 1) % clip.Count];
                var input = output;
                output = new List<Point2d>();

                if (input.Count == 0)
                    break;

                var s = input[input.Count - 1];
                foreach (var e in input)
                {
                    bool eInside = IsInside(e, a, b);
                    bool sInside = IsInside(s, a, b);

                    if (eInside)
                    {
                        if (!sInside)
                            output.Add(Intersection(s, e, a, b));
                        output.Add(e);
                    }
                    else if (sInside)
                    {
                        output.Add(Intersection(s, e, a, b));
                    }

                    s = e;
                }
            }

            return output;
        }

        private static bool IsInside(Point2d p, Point2d a, Point2d b)
        {
            return ((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X)) >= -Tolerance;
        }

        private static Point2d Intersection(Point2d p1, Point2d p2, Point2d a, Point2d b)
        {
            double x1 = p1.X, y1 = p1.Y;
            double x2 = p2.X, y2 = p2.Y;
            double x3 = a.X, y3 = a.Y;
            double x4 = b.X, y4 = b.Y;

            double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(denom) < Tolerance)
                return p2;

            double px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / denom;
            double py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / denom;
            return new Point2d(px, py);
        }

        private static bool IsConvex(List<Point2d> vertices)
        {
            bool hasPositive = false;
            bool hasNegative = false;

            for (int i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Count];
                var c = vertices[(i + 2) % vertices.Count];
                double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);

                if (cross > Tolerance) hasPositive = true;
                if (cross < -Tolerance) hasNegative = true;
                if (hasPositive && hasNegative) return false;
            }

            return true;
        }

        private static bool ContainsNear(List<double> values, double value)
        {
            if (values == null) return false;
            return values.Any(v => Math.Abs(v - value) <= Math.Max(1.0, Math.Abs(value) * 1e-6));
        }

        private static void SortAndDeduplicate(List<double> values)
        {
            values.Sort();
            for (int i = values.Count - 1; i > 0; i--)
            {
                if (Math.Abs(values[i] - values[i - 1]) <= Math.Max(1.0, Math.Abs(values[i]) * 1e-6))
                    values.RemoveAt(i);
            }
        }

        private static double SignedArea(List<Point2d> vertices)
        {
            if (vertices == null || vertices.Count < 3) return 0;

            double area = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Count];
                area += a.X * b.Y - b.X * a.Y;
            }

            return area * 0.5;
        }

        private static (double minX, double minY, double maxX, double maxY) GetExtents(List<Point2d> vertices)
        {
            return (
                vertices.Min(p => p.X),
                vertices.Min(p => p.Y),
                vertices.Max(p => p.X),
                vertices.Max(p => p.Y));
        }

        private static void DrawWallPiece(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            FillPiece piece)
        {
            var polyline = new Polyline();
            polyline.SetDatabaseDefaults(db);
            for (int i = 0; i < piece.Polygon.Count; i++)
                polyline.AddVertexAt(i, piece.Polygon[i], 0, 0, 0);
            polyline.Closed = true;
            polyline.Layer = piece.IsFullSheet ? "MC_MATERIAL_FILL" : "MC_MATERIAL_TRIM";
            polyline.ColorIndex = piece.IsFullSheet ? (short)3 : (short)30;
            ms.AppendEntity(polyline);
            tr.AddNewlyCreatedDBObject(polyline, true);

            DrawCenteredLabel(ms, tr, db, piece.PieceLabel, polyline.GeometricExtents, "MC_MATERIAL_LABELS", 7);
        }

        private static void DrawBoundarySummaries(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            List<List<Point2d>> boundaries,
            List<FillPiece> pieces,
            MaterialPlate plate)
        {
            for (int i = 0; i < boundaries.Count; i++)
            {
                var ext = GetExtents(boundaries[i]);
                int sheetRefs = pieces
                    .Where(p => p.BoundaryIndex == i)
                    .Select(p => p.SheetLabel)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                int trimPieces = pieces.Count(p => p.BoundaryIndex == i && !p.IsFullSheet);

                var info = new DBText();
                info.SetDatabaseDefaults(db);
                info.TextString = $"{plate.MaterialName} {plate.Code}: {sheetRefs} sheet ref(s), {trimPieces} trim piece(s)";
                info.Height = 25;
                info.Layer = "MC_MATERIAL_LABELS";
                info.ColorIndex = 7;
                info.Position = new Point3d(ext.minX, ext.maxY + Math.Max(50, plate.Height * 0.05), 0);
                ms.AppendEntity(info);
                tr.AddNewlyCreatedDBObject(info, true);
            }
        }

        private static void DrawRemainingOffcuts(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            List<List<Point2d>> boundaries,
            List<FillStockSheet> trimSheets,
            MaterialFillOptions options,
            MaterialFillResult result)
        {
            if (boundaries.Count == 0 || trimSheets.Count == 0)
                return;

            double minBoundaryX = boundaries.Min(b => b.Min(p => p.X));
            double maxBoundaryY = boundaries.Max(b => b.Max(p => p.Y));
            double cursorX = minBoundaryX - options.Plate.Width - Math.Max(250, options.Plate.Width * 0.15);
            double cursorY = maxBoundaryY;
            double rowGap = Math.Max(40, options.Plate.Height * 0.03);

            foreach (var sheet in trimSheets)
            {
                var usableOffcuts = sheet.FreeRects
                    .Where(r => r.Width > Tolerance && r.Height > Tolerance)
                    .OrderByDescending(r => r.Width * r.Height)
                    .ToList();

                if (usableOffcuts.Count == 0)
                    continue;

                string cutFrom = string.Join(", ", sheet.Pieces.Select(p => p.PieceLabel));
                DrawOffcutHeader(ms, tr, db, sheet.Label, cutFrom, cursorX, cursorY + 35);

                double localY = cursorY;
                foreach (var offcut in usableOffcuts)
                {
                    DrawOffcut(ms, tr, db, offcut, cursorX, localY - offcut.Height, sheet.Label, cutFrom);
                    localY -= offcut.Height + rowGap;
                    result.OffcutCount++;
                }

                cursorX -= options.Plate.Width + Math.Max(250, options.Plate.Width * 0.15);
            }
        }

        private static void DrawOffcutHeader(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            string sheetLabel,
            string cutFrom,
            double x,
            double y)
        {
            var header = new DBText();
            header.SetDatabaseDefaults(db);
            header.TextString = $"{sheetLabel} offcuts from {cutFrom}";
            header.Height = 22;
            header.Layer = "MC_MATERIAL_LABELS";
            header.ColorIndex = 7;
            header.Position = new Point3d(x, y, 0);
            ms.AppendEntity(header);
            tr.AddNewlyCreatedDBObject(header, true);
        }

        private static void DrawOffcut(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            FillFreeRect offcut,
            double x,
            double y,
            string sheetLabel,
            string cutFrom)
        {
            var polyline = new Polyline();
            polyline.SetDatabaseDefaults(db);
            polyline.AddVertexAt(0, new Point2d(x, y), 0, 0, 0);
            polyline.AddVertexAt(1, new Point2d(x + offcut.Width, y), 0, 0, 0);
            polyline.AddVertexAt(2, new Point2d(x + offcut.Width, y + offcut.Height), 0, 0, 0);
            polyline.AddVertexAt(3, new Point2d(x, y + offcut.Height), 0, 0, 0);
            polyline.Closed = true;
            polyline.Layer = "MC_MATERIAL_OFFCUT";
            polyline.ColorIndex = 2;
            ms.AppendEntity(polyline);
            tr.AddNewlyCreatedDBObject(polyline, true);

            string label = $"OFFCUT {sheetLabel}\nfrom {cutFrom}\n{offcut.Width:0.#} x {offcut.Height:0.#}";
            DrawCenteredLabel(ms, tr, db, label, polyline.GeometricExtents, "MC_MATERIAL_LABELS", 7);
        }

        private static void DrawCenteredLabel(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            string text,
            Extents3d ext,
            string layer,
            short colorIndex)
        {
            double width = ext.MaxPoint.X - ext.MinPoint.X;
            double height = ext.MaxPoint.Y - ext.MinPoint.Y;
            if (width < 80 || height < 30)
                return;

            double cx = (ext.MinPoint.X + ext.MaxPoint.X) / 2;
            double cy = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2;
            double textHeight = Math.Max(8, Math.Min(35, Math.Min(width, height) / 8));

            string[] lines = (text ?? "").Split(new[] { '\n' }, StringSplitOptions.None);
            double totalHeight = textHeight * lines.Length;
            for (int i = 0; i < lines.Length; i++)
            {
                var label = new DBText();
                label.SetDatabaseDefaults(db);
                label.TextString = lines[i];
                label.Height = textHeight;
                label.Layer = layer;
                label.ColorIndex = colorIndex;
                double y = cy + totalHeight / 2 - i * textHeight - textHeight / 2;
                label.Position = new Point3d(cx, y, 0);
                label.HorizontalMode = TextHorizontalMode.TextCenter;
                label.VerticalMode = TextVerticalMode.TextVerticalMid;
                label.AlignmentPoint = new Point3d(cx, y, 0);
                ms.AppendEntity(label);
                tr.AddNewlyCreatedDBObject(label, true);
            }
        }

        private static void EnsureLayers(Database db, Transaction tr)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            CreateLayerIfNotExists(lt, tr, "MC_MATERIAL_FILL", 3);
            CreateLayerIfNotExists(lt, tr, "MC_MATERIAL_TRIM", 30);
            CreateLayerIfNotExists(lt, tr, "MC_MATERIAL_OFFCUT", 2);
            CreateLayerIfNotExists(lt, tr, "MC_MATERIAL_LABELS", 7);
        }

        private static void CreateLayerIfNotExists(LayerTable lt, Transaction tr, string name, short colorIndex)
        {
            if (lt.Has(name)) return;

            var layer = new LayerTableRecord();
            layer.Name = name;
            layer.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            lt.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }
    }
}
