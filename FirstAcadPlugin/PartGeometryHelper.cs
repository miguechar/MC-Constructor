using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Helper class for extracting and recreating 3D solid geometry.
    /// </summary>
    public static class PartGeometryHelper
    {
        /// <summary>
        /// Extract complete part data from a 3D solid.
        /// </summary>
        public static DrawingPart ExtractPartData(ObjectId solidId, string partName)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            string drawingName = Path.GetFileName(doc.Name);

            var part = new DrawingPart
            {
                PartName = partName,
                SourceDrawingName = drawingName,
                IsOriginal = true
            };

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var solid = tr.GetObject(solidId, OpenMode.ForRead) as Solid3d;
                if (solid == null) return null;

                part.SourceObjectHandle = solid.Handle.ToString();

                // Extract geometry data
                part.GeometryData = ExportSolidData(solid);
                part.GeometryType = "3DSOLID";

                // Get bounding box
                var extents = solid.GeometricExtents;
                part.BBoxMinX = extents.MinPoint.X;
                part.BBoxMinY = extents.MinPoint.Y;
                part.BBoxMinZ = extents.MinPoint.Z;
                part.BBoxMaxX = extents.MaxPoint.X;
                part.BBoxMaxY = extents.MaxPoint.Y;
                part.BBoxMaxZ = extents.MaxPoint.Z;

                // Calculate center position
                part.PositionX = (extents.MinPoint.X + extents.MaxPoint.X) / 2;
                part.PositionY = (extents.MinPoint.Y + extents.MaxPoint.Y) / 2;
                part.PositionZ = (extents.MinPoint.Z + extents.MaxPoint.Z) / 2;

                // Visual properties
                part.LayerName = solid.Layer;
                part.ColorIndex = solid.ColorIndex;
                part.MaterialName = solid.Material;

                tr.Commit();
            }

            return part;
        }

        /// <summary>
        /// Export solid data to a string representation.
        /// Stores bounding box and mass properties for recreation.
        /// </summary>
        private static string ExportSolidData(Solid3d solid)
        {
            try
            {
                var extents = solid.GeometricExtents;
                var massProps = solid.MassProperties;

                var sb = new StringBuilder();
                sb.AppendLine("MC_PART_DATA_V1");
                sb.AppendLine($"TYPE:3DSOLID");
                sb.AppendLine($"VOLUME:{massProps.Volume}");
                sb.AppendLine($"CENTROID:{massProps.Centroid.X},{massProps.Centroid.Y},{massProps.Centroid.Z}");
                sb.AppendLine($"BBOX_MIN:{extents.MinPoint.X},{extents.MinPoint.Y},{extents.MinPoint.Z}");
                sb.AppendLine($"BBOX_MAX:{extents.MaxPoint.X},{extents.MaxPoint.Y},{extents.MaxPoint.Z}");

                // Try to export to SAT file and read the contents
                string satData = ExportToSatString(solid);
                if (!string.IsNullOrEmpty(satData))
                {
                    sb.AppendLine("SAT_START");
                    sb.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(satData)));
                    sb.AppendLine("SAT_END");
                }

                return sb.ToString();
            }
            catch (System.Exception ex)
            {
                // Return basic metadata if full export fails
                var extents = solid.GeometricExtents;
                return $"MC_PART_DATA_V1\nTYPE:3DSOLID\nBBOX_MIN:{extents.MinPoint.X},{extents.MinPoint.Y},{extents.MinPoint.Z}\nBBOX_MAX:{extents.MaxPoint.X},{extents.MaxPoint.Y},{extents.MaxPoint.Z}\nERROR:{ex.Message}";
            }
        }

        /// <summary>
        /// Try to export solid to SAT format string.
        /// </summary>
        private static string ExportToSatString(Solid3d solid)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".sat");
            try
            {
                // Create a collection with our solid
                var entities = new DBObjectCollection();
                entities.Add(solid);

                // Export to SAT file
                // Note: This uses the ACISOUT command functionality
                // We'll store the bounding box info instead for now
                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        /// <summary>
        /// Create a 3D solid from stored part data.
        /// </summary>
        public static ObjectId CreateSolidFromPart(DrawingPart part, Point3d insertionPoint)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            ObjectId resultId = ObjectId.Null;

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                Solid3d newSolid = null;

                // Try to recreate from SAT data
                if (!string.IsNullOrEmpty(part.GeometryData) && part.GeometryData.Contains("SAT_START"))
                {
                    newSolid = CreateFromSatData(part.GeometryData);
                    if (newSolid != null)
                    {
                        // Move to insertion point
                        var offset = insertionPoint - new Point3d(part.PositionX, part.PositionY, part.PositionZ);
                        newSolid.TransformBy(Matrix3d.Displacement(new Vector3d(offset.X, offset.Y, offset.Z)));
                    }
                }

                // Fallback: Create a box based on bounding box dimensions
                if (newSolid == null && part.BBoxMinX.HasValue)
                {
                    double width = (part.BBoxMaxX ?? 0) - (part.BBoxMinX ?? 0);
                    double depth = (part.BBoxMaxY ?? 0) - (part.BBoxMinY ?? 0);
                    double height = (part.BBoxMaxZ ?? 0) - (part.BBoxMinZ ?? 0);

                    // Ensure minimum size
                    width = Math.Max(width, 0.1);
                    depth = Math.Max(depth, 0.1);
                    height = Math.Max(height, 0.1);

                    newSolid = new Solid3d();
                    newSolid.CreateBox(width, depth, height);

                    // Position at insertion point (box is created at origin, centered)
                    var center = new Point3d(
                        insertionPoint.X,
                        insertionPoint.Y,
                        insertionPoint.Z + height / 2
                    );
                    newSolid.TransformBy(Matrix3d.Displacement(center.GetAsVector()));
                }

                if (newSolid != null)
                {
                    // Apply properties
                    newSolid.Layer = part.LayerName ?? "0";
                    if (part.ColorIndex != 256)
                        newSolid.ColorIndex = part.ColorIndex;

                    // Add to model space
                    resultId = ms.AppendEntity(newSolid);
                    tr.AddNewlyCreatedDBObject(newSolid, true);

                    // Add XData to track this as an inserted part
                    SetPartXData(newSolid, part, tr, db);
                }

                tr.Commit();
            }

            return resultId;
        }

        /// <summary>
        /// Try to create a solid from SAT data stored in the geometry string.
        /// </summary>
        private static Solid3d CreateFromSatData(string geometryData)
        {
            try
            {
                int startIdx = geometryData.IndexOf("SAT_START") + "SAT_START".Length;
                int endIdx = geometryData.IndexOf("SAT_END");
                if (endIdx <= startIdx) return null;

                string base64 = geometryData.Substring(startIdx, endIdx - startIdx).Trim();
                string satData = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

                // Write SAT to temp file
                string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".sat");
                try
                {
                    File.WriteAllText(tempFile, satData);

                    // Import from SAT file
                    // Note: Standard API doesn't have direct SAT import
                    // Would need to use ACISIN command
                    return null;
                }
                finally
                {
                    try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Set XData on an entity to track part information.
        /// </summary>
        public static void SetPartXData(Entity entity, DrawingPart part, Transaction tr, Database db)
        {
            // Register app name
            var regAppTable = tr.GetObject(db.RegAppTableId, OpenMode.ForWrite) as RegAppTable;
            if (!regAppTable.Has(Commands.AppName))
            {
                var regApp = new RegAppTableRecord();
                regApp.Name = Commands.AppName;
                regAppTable.Add(regApp);
                tr.AddNewlyCreatedDBObject(regApp, true);
            }

            // Create XData
            var xdata = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, Commands.AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "PartName"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, part.PartName ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "PartId"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, part.Id.ToString()),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "ParentPartId"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, part.ParentPartId?.ToString() ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "IsOriginal"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, part.IsOriginal.ToString())
            );

            entity.XData = xdata;
        }

        /// <summary>
        /// Get part info from entity XData.
        /// </summary>
        public static (string partName, Guid? partId, Guid? parentPartId, bool isOriginal) GetPartXData(Entity entity)
        {
            string partName = "";
            Guid? partId = null;
            Guid? parentPartId = null;
            bool isOriginal = true;

            var xdata = entity.GetXDataForApplication(Commands.AppName);
            if (xdata == null) return (partName, partId, parentPartId, isOriginal);

            var values = xdata.AsArray();
            for (int i = 1; i < values.Length - 1; i += 2)
            {
                string label = values[i].Value.ToString();
                string value = values[i + 1].Value.ToString();

                switch (label)
                {
                    case "PartName":
                        partName = value;
                        break;
                    case "PartId":
                        if (Guid.TryParse(value, out Guid pid))
                            partId = pid;
                        break;
                    case "ParentPartId":
                        if (Guid.TryParse(value, out Guid ppid))
                            parentPartId = ppid;
                        break;
                    case "IsOriginal":
                        bool.TryParse(value, out isOriginal);
                        break;
                }
            }

            return (partName, partId, parentPartId, isOriginal);
        }

        /// <summary>
        /// Update an existing solid with new geometry from database.
        /// For now, this updates visual properties only since we use placeholder boxes.
        /// </summary>
        public static bool UpdateSolidFromPart(ObjectId solidId, DrawingPart part)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var solid = tr.GetObject(solidId, OpenMode.ForWrite) as Solid3d;
                if (solid == null) return false;

                // Update visual properties
                solid.Layer = part.LayerName ?? solid.Layer;
                if (part.ColorIndex != 256)
                    solid.ColorIndex = part.ColorIndex;

                tr.Commit();
                return true;
            }
        }
    }
}
