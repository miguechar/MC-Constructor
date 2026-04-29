using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.IO;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcadDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Helpers for capturing and recreating part geometry. The strategy is:
    /// the source entity is Wblock'd into a tiny side-database that gets
    /// saved to a temp .dwg, and the .dwg bytes are base64-encoded into the
    /// drawing_parts.geometry_data TEXT column. To recreate, the bytes are
    /// written back to a temp .dwg, opened as a side-database, and the
    /// modelspace entities are cloned into the target database via
    /// WblockCloneObjects. This preserves curves, holes, ACIS BREP, etc.
    /// exactly - the previous "build a box from the bbox" path was the
    /// reason rounded parts came back as squares.
    /// </summary>
    public static class PartGeometryHelper
    {
        // Marker used by the new geometry storage format. Anything starting
        // with this prefix is a base64-encoded .dwg blob; older "MC_PART_DATA_V1"
        // payloads still parse via the legacy bbox-fallback path so existing
        // saved parts keep working.
        public const string DwgFormatPrefix = "MC_DWG_V1:";

        // ====================================================================
        // EXTRACT
        // ====================================================================

        /// <summary>
        /// Capture a DrawingPart record from any entity (Solid3d, Polyline,
        /// Region, Circle, Ellipse, ...). Bbox/transform are recorded for
        /// fast preview, and the full geometry is serialized as a Wblock'd
        /// .dwg blob for high-fidelity recreation.
        /// </summary>
        public static DrawingPart ExtractPartData(ObjectId entityId, string partName)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return null;
            var db = doc.Database;
            string drawingName = Path.GetFileName(doc.Name);

            DrawingPart part;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
                if (entity == null) return null;

                part = new DrawingPart
                {
                    PartName = partName,
                    SourceDrawingName = drawingName,
                    SourceObjectHandle = entity.Handle.ToString(),
                    IsOriginal = true,
                    GeometryType = entity.GetType().Name,
                    LayerName = entity.Layer,
                    ColorIndex = entity.ColorIndex,
                };

                // Bounding box - best-effort. A few entity types throw on
                // GeometricExtents; we still want to record whatever we can.
                try
                {
                    var ext = entity.GeometricExtents;
                    part.BBoxMinX = ext.MinPoint.X;
                    part.BBoxMinY = ext.MinPoint.Y;
                    part.BBoxMinZ = ext.MinPoint.Z;
                    part.BBoxMaxX = ext.MaxPoint.X;
                    part.BBoxMaxY = ext.MaxPoint.Y;
                    part.BBoxMaxZ = ext.MaxPoint.Z;
                    part.PositionX = (ext.MinPoint.X + ext.MaxPoint.X) / 2;
                    part.PositionY = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2;
                    part.PositionZ = (ext.MinPoint.Z + ext.MaxPoint.Z) / 2;
                }
                catch { }

                // Material is only on Solid3d/Surface.
                if (entity is Solid3d s3d) part.MaterialName = s3d.Material;

                tr.Commit();
            }

            // Wblock OUTSIDE the transaction - it starts its own internally.
            try
            {
                byte[] dwgBytes = SerializeEntityToDwgBytes(entityId);
                if (dwgBytes != null && dwgBytes.Length > 0)
                    part.GeometryData = DwgFormatPrefix + Convert.ToBase64String(dwgBytes);
            }
            catch
            {
                // Best-effort: if serialization fails the bbox is still
                // recorded and CreateEntityFromPart will fall back to a
                // placeholder box.
            }

            return part;
        }

        /// <summary>
        /// Serialize a single entity to a .dwg blob via Database.Wblock + a
        /// temp file. Returns null on failure.
        /// </summary>
        public static byte[] SerializeEntityToDwgBytes(ObjectId entityId)
        {
            if (entityId.IsNull || !entityId.IsValid) return null;

            var sourceDb = entityId.Database;
            if (sourceDb == null) return null;

            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dwg");
            Database wblockDb = null;
            try
            {
                var ids = new ObjectIdCollection();
                ids.Add(entityId);

                wblockDb = sourceDb.Wblock(ids, Point3d.Origin);
                wblockDb.SaveAs(tempFile, DwgVersion.Current);

                return File.ReadAllBytes(tempFile);
            }
            finally
            {
                if (wblockDb != null)
                {
                    try { wblockDb.Dispose(); } catch { }
                }
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        /// <summary>
        /// If geometry_data is in the new dwg-blob format, decode and return
        /// the raw .dwg bytes. Otherwise return null and let the caller fall
        /// back to the bbox-based placeholder.
        /// </summary>
        public static byte[] TryGetDwgBytes(string geometryData)
        {
            if (string.IsNullOrEmpty(geometryData)) return null;
            if (!geometryData.StartsWith(DwgFormatPrefix, StringComparison.Ordinal)) return null;

            try
            {
                return Convert.FromBase64String(geometryData.Substring(DwgFormatPrefix.Length).Trim());
            }
            catch
            {
                return null;
            }
        }

        // ====================================================================
        // RECREATE
        // ====================================================================

        /// <summary>
        /// Clone all modelspace entities from a serialized .dwg blob into
        /// the target db's modelspace, applying <paramref name="xform"/> to
        /// each clone. Optionally forces clones onto a specific layer/color.
        /// Returns the cloned entity ObjectIds in target db order.
        ///
        /// Caller MUST NOT have an open transaction over targetDb when this
        /// is called - WblockCloneObjects works at the database level and
        /// the post-process walk opens its own transaction.
        /// </summary>
        public static List<ObjectId> CloneGeometryIntoDb(
            byte[] dwgBytes,
            Database targetDb,
            Matrix3d xform,
            string targetLayer = null,
            int? targetColorIndex = null)
        {
            var clonedIds = new List<ObjectId>();
            if (dwgBytes == null || dwgBytes.Length == 0 || targetDb == null) return clonedIds;

            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                File.WriteAllBytes(tempFile, dwgBytes);

                using (var sideDb = new Database(false, true))
                {
                    sideDb.ReadDwgFile(tempFile, FileShare.ReadWrite, true, "");
                    sideDb.CloseInput(true);

                    // Collect modelspace entity ids in the side-db.
                    var sourceIds = new ObjectIdCollection();
                    using (var trS = sideDb.TransactionManager.StartTransaction())
                    {
                        var btS = (BlockTable)trS.GetObject(sideDb.BlockTableId, OpenMode.ForRead);
                        var msS = (BlockTableRecord)trS.GetObject(btS[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                        foreach (ObjectId id in msS) sourceIds.Add(id);
                        trS.Commit();
                    }
                    if (sourceIds.Count == 0) return clonedIds;

                    // Resolve the target modelspace.
                    ObjectId targetMsId;
                    using (var trT = targetDb.TransactionManager.StartTransaction())
                    {
                        var btT = (BlockTable)trT.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                        targetMsId = btT[BlockTableRecord.ModelSpace];
                        trT.Commit();
                    }

                    var idMap = new IdMapping();
                    sideDb.WblockCloneObjects(
                        sourceIds,
                        targetMsId,
                        idMap,
                        DuplicateRecordCloning.Replace,
                        false);

                    // Walk the clones to apply transform + layer/color.
                    using (var trT = targetDb.TransactionManager.StartTransaction())
                    {
                        foreach (IdPair pair in idMap)
                        {
                            if (!pair.IsCloned || !pair.IsPrimary) continue;
                            if (pair.Value.IsNull) continue;

                            var clone = trT.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
                            if (clone == null) continue;

                            try { clone.TransformBy(xform); } catch { }

                            if (!string.IsNullOrEmpty(targetLayer))
                            {
                                try { clone.Layer = targetLayer; } catch { }
                            }
                            if (targetColorIndex.HasValue)
                            {
                                try { clone.ColorIndex = targetColorIndex.Value; } catch { }
                            }

                            clonedIds.Add(pair.Value);
                        }
                        trT.Commit();
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }

            return clonedIds;
        }

        /// <summary>
        /// Recreate a part from its stored geometry at <paramref name="insertionPoint"/>.
        /// New format: clones the original entity exactly (curves preserved).
        /// Legacy format (or missing geometry): falls back to a 3D placeholder
        /// box sized to the stored bounding box. Returns the primary clone's
        /// ObjectId, or ObjectId.Null on failure.
        /// </summary>
        public static ObjectId CreateEntityFromPart(DrawingPart part, Point3d insertionPoint)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return ObjectId.Null;
            var db = doc.Database;

            byte[] dwgBytes = TryGetDwgBytes(part.GeometryData);
            if (dwgBytes != null && part.BBoxMinX.HasValue)
            {
                // Translate so the part's bbox-min lands at the insertion
                // point. Z is preserved relative to bbox-min.
                var xform = Matrix3d.Displacement(new Vector3d(
                    insertionPoint.X - (part.BBoxMinX ?? 0),
                    insertionPoint.Y - (part.BBoxMinY ?? 0),
                    insertionPoint.Z - (part.BBoxMinZ ?? 0)));

                using (doc.LockDocument())
                {
                    var clones = CloneGeometryIntoDb(dwgBytes, db, xform);
                    ObjectId firstId = clones.Count > 0 ? clones[0] : ObjectId.Null;

                    if (firstId != ObjectId.Null)
                    {
                        // Stamp XData on the primary clone so MCOverridePart
                        // / MCUpdateAllParts can find the part later.
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var ent = tr.GetObject(firstId, OpenMode.ForWrite) as Entity;
                            if (ent != null) SetPartXData(ent, part, tr, db);
                            tr.Commit();
                        }
                    }

                    return firstId;
                }
            }

            // Legacy fallback: build a placeholder 3D box.
            return CreateLegacyPlaceholderBox(part, insertionPoint, db, doc);
        }

        /// <summary>
        /// Backwards-compat alias. Existing callers (MCInsertPart) keep
        /// working unchanged - the underlying logic now picks the right
        /// recreation path based on the stored geometry format.
        /// </summary>
        public static ObjectId CreateSolidFromPart(DrawingPart part, Point3d insertionPoint)
        {
            return CreateEntityFromPart(part, insertionPoint);
        }

        private static ObjectId CreateLegacyPlaceholderBox(DrawingPart part, Point3d insertionPoint, Database db, AcadDocument doc)
        {
            if (!part.BBoxMinX.HasValue) return ObjectId.Null;

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                double width  = Math.Max(0.1, (part.BBoxMaxX ?? 0) - (part.BBoxMinX ?? 0));
                double depth  = Math.Max(0.1, (part.BBoxMaxY ?? 0) - (part.BBoxMinY ?? 0));
                double height = Math.Max(0.1, (part.BBoxMaxZ ?? 0) - (part.BBoxMinZ ?? 0));

                var solid = new Solid3d();
                solid.CreateBox(width, depth, height);

                var center = new Point3d(insertionPoint.X, insertionPoint.Y, insertionPoint.Z + height / 2);
                solid.TransformBy(Matrix3d.Displacement(center.GetAsVector()));

                solid.Layer = part.LayerName ?? "0";
                if (part.ColorIndex != 256) solid.ColorIndex = part.ColorIndex;

                ObjectId id = ms.AppendEntity(solid);
                tr.AddNewlyCreatedDBObject(solid, true);
                SetPartXData(solid, part, tr, db);
                tr.Commit();
                return id;
            }
        }

        // ====================================================================
        // XDATA
        // ====================================================================

        public static void SetPartXData(Entity entity, DrawingPart part, Transaction tr, Database db)
        {
            var regAppTable = tr.GetObject(db.RegAppTableId, OpenMode.ForWrite) as RegAppTable;
            if (!regAppTable.Has(Commands.AppName))
            {
                var regApp = new RegAppTableRecord { Name = Commands.AppName };
                regAppTable.Add(regApp);
                tr.AddNewlyCreatedDBObject(regApp, true);
            }

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

        // ====================================================================
        // UPDATE
        // ====================================================================

        /// <summary>
        /// Update the visual properties of an existing entity from the part
        /// record. Geometry replacement is intentionally not done here - to
        /// pick up new geometry from an override, erase the old entity and
        /// re-insert via CreateEntityFromPart.
        /// </summary>
        public static bool UpdateSolidFromPart(ObjectId entityId, DrawingPart part)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(entityId, OpenMode.ForWrite) as Entity;
                if (entity == null) return false;

                if (!string.IsNullOrEmpty(part.LayerName)) entity.Layer = part.LayerName;
                if (part.ColorIndex != 256) entity.ColorIndex = part.ColorIndex;

                tr.Commit();
                return true;
            }
        }
    }
}
