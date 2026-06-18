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
    }

    public class MaterialFillResult
    {
        public int BoundaryCount { get; set; }
        public int SheetCount { get; set; }
        public int FullSheets { get; set; }
        public int TrimmedSheets { get; set; }
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

    public static class MaterialFillService
    {
        private const double Tolerance = 1e-6;

        public static MaterialFillResult FillBoundaries(
            Database db,
            IEnumerable<MaterialFillBoundary> boundaries,
            MaterialFillOptions options)
        {
            var result = new MaterialFillResult();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayers(db, tr);

                ObjectId modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var ms = (BlockTableRecord)tr.GetObject(modelSpaceId, OpenMode.ForWrite);

                foreach (var boundary in boundaries)
                {
                    if (boundary.Vertices == null || boundary.Vertices.Count < 3 || !IsConvex(boundary.Vertices))
                    {
                        result.SkippedBoundaries++;
                        continue;
                    }

                    result.BoundaryCount++;
                    FillOneBoundary(ms, tr, db, boundary.Vertices, options, result);
                }

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

        private static void FillOneBoundary(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            List<Point2d> boundary,
            MaterialFillOptions options,
            MaterialFillResult result)
        {
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
            double boundaryArea = Math.Abs(SignedArea(boundary));
            result.BoundaryArea += boundaryArea;
            int startSheetCount = result.SheetCount;
            int startTrimmedCount = result.TrimmedSheets;

            int index = 1;
            for (double y = minY; y < maxY - Tolerance; y += stepY)
            {
                for (double x = minX; x < maxX - Tolerance; x += stepX)
                {
                    var rect = new List<Point2d>
                    {
                        new Point2d(x, y),
                        new Point2d(x + sheetWidth, y),
                        new Point2d(x + sheetWidth, y + sheetHeight),
                        new Point2d(x, y + sheetHeight)
                    };

                    var clipped = ClipPolygon(rect, boundary);
                    double area = Math.Abs(SignedArea(clipped));
                    if (clipped.Count < 3 || area < Tolerance)
                        continue;

                    bool isFull = Math.Abs(area - sheetWidth * sheetHeight) <= Math.Max(1.0, sheetWidth * sheetHeight * 0.0001);
                    DrawSheet(ms, tr, db, clipped, isFull, options.Plate, index++);

                    result.SheetCount++;
                    if (isFull) result.FullSheets++;
                    else result.TrimmedSheets++;
                    result.PlacedArea += area;
                }
            }

            DrawSummary(ms, tr, db, options.Plate, minX, maxY + Math.Max(50, sheetHeight * 0.05),
                result.SheetCount - startSheetCount,
                result.TrimmedSheets - startTrimmedCount);
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

        private static void DrawSheet(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            List<Point2d> vertices,
            bool isFull,
            MaterialPlate plate,
            int index)
        {
            var polyline = new Polyline();
            polyline.SetDatabaseDefaults(db);
            for (int i = 0; i < vertices.Count; i++)
                polyline.AddVertexAt(i, vertices[i], 0, 0, 0);
            polyline.Closed = true;
            polyline.Layer = isFull ? "MC_MATERIAL_FILL" : "MC_MATERIAL_TRIM";
            polyline.ColorIndex = isFull ? (short)3 : (short)30;
            ms.AppendEntity(polyline);
            tr.AddNewlyCreatedDBObject(polyline, true);

            var ext = polyline.GeometricExtents;
            double width = ext.MaxPoint.X - ext.MinPoint.X;
            double height = ext.MaxPoint.Y - ext.MinPoint.Y;
            if (width < 80 || height < 30)
                return;

            double cx = (ext.MinPoint.X + ext.MaxPoint.X) / 2;
            double cy = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2;
            double textHeight = Math.Max(8, Math.Min(35, Math.Min(width, height) / 8));

            var label = new DBText();
            label.SetDatabaseDefaults(db);
            label.TextString = $"{plate.Code}-{index}{(isFull ? "" : " TRIM")}";
            label.Height = textHeight;
            label.Layer = "MC_MATERIAL_LABELS";
            label.ColorIndex = 7;
            label.Position = new Point3d(cx, cy, 0);
            label.HorizontalMode = TextHorizontalMode.TextCenter;
            label.VerticalMode = TextVerticalMode.TextVerticalMid;
            label.AlignmentPoint = new Point3d(cx, cy, 0);
            ms.AppendEntity(label);
            tr.AddNewlyCreatedDBObject(label, true);
        }

        private static void DrawSummary(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            MaterialPlate plate,
            double x,
            double y,
            int totalSheets,
            int trimmedSheets)
        {
            var info = new DBText();
            info.SetDatabaseDefaults(db);
            info.TextString = $"{plate.MaterialName} {plate.Code}: {totalSheets} sheet(s), {trimmedSheets} trimmed";
            info.Height = 25;
            info.Layer = "MC_MATERIAL_LABELS";
            info.ColorIndex = 7;
            info.Position = new Point3d(x, y, 0);
            ms.AppendEntity(info);
            tr.AddNewlyCreatedDBObject(info, true);
        }

        private static void EnsureLayers(Database db, Transaction tr)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            CreateLayerIfNotExists(lt, tr, "MC_MATERIAL_FILL", 3);
            CreateLayerIfNotExists(lt, tr, "MC_MATERIAL_TRIM", 30);
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
