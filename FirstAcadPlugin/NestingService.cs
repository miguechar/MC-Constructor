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
    /// Represents a part to be nested with its bounding dimensions.
    /// </summary>
    public class NestingPart
    {
        public ObjectId SourceObjectId { get; set; }
        public string PartName { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Thickness { get; set; }
        public Point3d OriginalMin { get; set; }
        public Point3d OriginalMax { get; set; }

        public double NestedX { get; set; }
        public double NestedY { get; set; }
        public bool IsPlaced { get; set; }
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
                    if (!(entity is Solid3d solid)) continue;

                    var extents = solid.GeometricExtents;
                    double width = extents.MaxPoint.X - extents.MinPoint.X;
                    double depth = extents.MaxPoint.Y - extents.MinPoint.Y;
                    double height = extents.MaxPoint.Z - extents.MinPoint.Z;

                    string partName = $"Part_{parts.Count + 1}";
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
                                    partName = name;
                                break;
                            }
                        }
                    }

                    parts.Add(new NestingPart
                    {
                        SourceObjectId = selObj.ObjectId,
                        PartName = partName,
                        Width = width,
                        Height = depth,
                        Thickness = height,
                        OriginalMin = extents.MinPoint,
                        OriginalMax = extents.MaxPoint
                    });
                }

                tr.Commit();
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
        /// Create a new drawing with the nested parts.
        /// Uses docMgr.Add() to create a proper new drawing then writes entities directly.
        /// </summary>
        public static string CreateNestingDrawing(NestingResult nestingResult)
        {
            var docMgr = AcadApp.DocumentManager;

            // Create a new document - using empty string creates a default drawing
            Document newDoc = docMgr.Add("");

            // Make it the active document
            docMgr.MdiActiveDocument = newDoc;

            using (DocumentLock docLock = newDoc.LockDocument())
            {
                Database db = newDoc.Database;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Use SymbolUtilityServices to safely get model space - this is the safe way
                    ObjectId modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(modelSpaceId, OpenMode.ForWrite);

                    // Create layers
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                    CreateLayerIfNotExists(lt, tr, "MC_PLATE", 1);
                    CreateLayerIfNotExists(lt, tr, "MC_PARTS", 3);
                    CreateLayerIfNotExists(lt, tr, "MC_LABELS", 7);

                    // Draw plate outline (red rectangle)
                    Polyline plateOutline = new Polyline();
                    plateOutline.SetDatabaseDefaults(db);
                    plateOutline.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    plateOutline.AddVertexAt(1, new Point2d(nestingResult.PlateWidth, 0), 0, 0, 0);
                    plateOutline.AddVertexAt(2, new Point2d(nestingResult.PlateWidth, nestingResult.PlateHeight), 0, 0, 0);
                    plateOutline.AddVertexAt(3, new Point2d(0, nestingResult.PlateHeight), 0, 0, 0);
                    plateOutline.Closed = true;
                    plateOutline.Layer = "MC_PLATE";
                    plateOutline.ColorIndex = 1;
                    ms.AppendEntity(plateOutline);
                    tr.AddNewlyCreatedDBObject(plateOutline, true);

                    // Draw each nested part
                    foreach (var part in nestingResult.PlacedParts)
                    {
                        // Part outline (green rectangle)
                        Polyline partOutline = new Polyline();
                        partOutline.SetDatabaseDefaults(db);
                        partOutline.AddVertexAt(0, new Point2d(part.NestedX, part.NestedY), 0, 0, 0);
                        partOutline.AddVertexAt(1, new Point2d(part.NestedX + part.Width, part.NestedY), 0, 0, 0);
                        partOutline.AddVertexAt(2, new Point2d(part.NestedX + part.Width, part.NestedY + part.Height), 0, 0, 0);
                        partOutline.AddVertexAt(3, new Point2d(part.NestedX, part.NestedY + part.Height), 0, 0, 0);
                        partOutline.Closed = true;
                        partOutline.Layer = "MC_PARTS";
                        partOutline.ColorIndex = 3;
                        ms.AppendEntity(partOutline);
                        tr.AddNewlyCreatedDBObject(partOutline, true);

                        // Part name label (centered)
                        double labelX = part.NestedX + part.Width / 2;
                        double labelY = part.NestedY + part.Height / 2;
                        double textHeight = Math.Max(5, Math.Min(20, Math.Min(part.Width, part.Height) / 8));

                        DBText text = new DBText();
                        text.SetDatabaseDefaults(db);
                        text.TextString = part.PartName;
                        text.Height = textHeight;
                        text.Layer = "MC_LABELS";
                        text.ColorIndex = 7;
                        text.Position = new Point3d(labelX, labelY, 0);
                        text.HorizontalMode = TextHorizontalMode.TextCenter;
                        text.VerticalMode = TextVerticalMode.TextVerticalMid;
                        text.AlignmentPoint = new Point3d(labelX, labelY, 0);
                        ms.AppendEntity(text);
                        tr.AddNewlyCreatedDBObject(text, true);

                        // Dimension label below
                        DBText dimText = new DBText();
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

                    // Info text above plate
                    DBText infoText = new DBText();
                    infoText.SetDatabaseDefaults(db);
                    infoText.TextString = $"Plate: {nestingResult.PlateWidth} x {nestingResult.PlateHeight} mm  |  Parts: {nestingResult.PlacedParts.Count}  |  Efficiency: {nestingResult.Efficiency:F1}%";
                    infoText.Height = 20;
                    infoText.Layer = "MC_LABELS";
                    infoText.ColorIndex = 7;
                    infoText.Position = new Point3d(0, nestingResult.PlateHeight + 30, 0);
                    ms.AppendEntity(infoText);
                    tr.AddNewlyCreatedDBObject(infoText, true);

                    tr.Commit();
                }

                // Force regen and zoom to show the new geometry
                try
                {
                    newDoc.Editor.Regen();
                    newDoc.Editor.Command("_.ZOOM", "_E");
                }
                catch { }
            }

            return newDoc.Name;
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
