using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.IO;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcadDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace MCConstructor
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
        // with this prefix is a base64-encoded .dwg blob; older
        // "MC_PART_DATA_V1" payloads still parse via the legacy bbox-fallback
        // path so existing saved parts keep working.
        public const string DwgFormatPrefix = "MC_DWG_V1:";

        // ====================================================================
        // DIAGNOSTICS
        // ====================================================================

        /// <summary>
        /// Toggle for command-line diagnostics. Leave true while diagnosing
        /// deployment issues on another machine; set false later if you want
        /// a quiet release build again.
        /// </summary>
        private const bool EnableDiagnostics = true;

        private static void Log(string message)
        {
            if (!EnableDiagnostics) return;

            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                doc?.Editor?.WriteMessage($"\n[PartGeometryHelper] {message}");
            }
            catch
            {
                // Never let diagnostics break the actual command flow.
            }
        }

        private static string SafeEntityTypeName(ObjectId entityId)
        {
            try
            {
                var db = entityId.Database;
                if (db == null) return "<no-db>";

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(entityId, OpenMode.ForRead, false) as Entity;
                    string name = ent?.GetType().FullName ?? "<not-entity>";
                    tr.Commit();
                    return name;
                }
            }
            catch (Exception ex)
            {
                return $"<type-read-failed: {ex.Message}>";
            }
        }

        private static string SafeObjectHandle(ObjectId entityId)
        {
            try
            {
                if (entityId.IsNull || !entityId.IsValid) return "<invalid>";
                using (var tr = entityId.Database.TransactionManager.StartTransaction())
                {
                    var dbObj = tr.GetObject(entityId, OpenMode.ForRead, false);
                    string handle = dbObj?.Handle.ToString() ?? "<no-handle>";
                    tr.Commit();
                    return handle;
                }
            }
            catch
            {
                return "<handle-read-failed>";
            }
        }

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
            if (doc == null)
            {
                Log("ExtractPartData aborted: no active document.");
                return null;
            }

            var db = doc.Database;
            string drawingName = Path.GetFileName(doc.Name);

            DrawingPart part;

            Log(
                $"ExtractPartData start: id={entityId}, " +
                $"handle={SafeObjectHandle(entityId)}, " +
                $"type={SafeEntityTypeName(entityId)}, " +
                $"partName='{partName ?? ""}'");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
                if (entity == null)
                {
                    Log($"ExtractPartData aborted: object {entityId} is not an Entity.");
                    return null;
                }

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

                // Pull MaterialId out of XData if present.
                var xd = entity.GetXDataForApplication(Commands.AppName);
                if (xd != null)
                {
                    var xv = xd.AsArray();
                    for (int xi = 1; xi + 1 < xv.Length; xi += 2)
                    {
                        if (xv[xi].Value.ToString() == "MaterialId" &&
                            Guid.TryParse(xv[xi + 1].Value.ToString(), out Guid matId))
                        {
                            part.MaterialId = matId;
                            break;
                        }
                    }
                }

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

                    Log(
                        $"ExtractPartData extents ok: " +
                        $"min=({ext.MinPoint.X:F3}, {ext.MinPoint.Y:F3}, {ext.MinPoint.Z:F3}) " +
                        $"max=({ext.MaxPoint.X:F3}, {ext.MaxPoint.Y:F3}, {ext.MaxPoint.Z:F3})");
                }
                catch (Exception ex)
                {
                    Log($"ExtractPartData extents failed for {entityId}: {ex.Message}");
                }

                // Material is only on Solid3d/Surface.
                if (entity is Solid3d s3d) part.MaterialName = s3d.Material;

                tr.Commit();
            }

            // Wblock OUTSIDE the transaction - it starts its own internally.
            try
            {
                byte[] dwgBytes = SerializeEntityToDwgBytes(entityId);
                if (dwgBytes != null && dwgBytes.Length > 0)
                {
                    part.GeometryData = DwgFormatPrefix +
                                        Convert.ToBase64String(dwgBytes);
                    Log(
                        $"ExtractPartData geometry capture ok: " +
                        $"entity={entityId}, bytes={dwgBytes.Length}");
                }
                else
                {
                    Log(
                        $"ExtractPartData geometry capture returned no bytes: " +
                        $"entity={entityId}. Caller will fall back later if needed.");
                }
            }
            catch (Exception ex)
            {
                Log(
                    $"ExtractPartData geometry capture threw for entity={entityId}, " +
                    $"type={SafeEntityTypeName(entityId)}: {ex}");
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
    if (entityId.IsNull || !entityId.IsValid)
    {
        Log("SerializeEntityToDwgBytes aborted: invalid ObjectId.");
        return null;
    }

    var sourceDb = entityId.Database;
    if (sourceDb == null)
    {
        Log($"SerializeEntityToDwgBytes aborted: sourceDb is null for {entityId}.");
        return null;
    }

    var doc = AcadApp.DocumentManager.MdiActiveDocument;
    if (doc == null)
    {
        Log($"SerializeEntityToDwgBytes aborted: no active document for {entityId}.");
        return null;
    }

    string entityType = SafeEntityTypeName(entityId);
    string entityHandle = SafeObjectHandle(entityId);
    string tempFile = Path.Combine(
        Path.GetTempPath(),
        Guid.NewGuid().ToString("N") + ".dwg");

    Database wblockDb = null;
    try
    {
        var ids = new ObjectIdCollection();
        ids.Add(entityId);

        Log(
            $"SerializeEntityToDwgBytes start: id={entityId}, " +
            $"handle={entityHandle}, type={entityType}, temp='{tempFile}'");

        using (doc.LockDocument())
        {
            wblockDb = sourceDb.Wblock(ids, Point3d.Origin);
        }

        if (wblockDb == null)
        {
            Log(
                $"SerializeEntityToDwgBytes failed: Wblock returned null " +
                $"for id={entityId}, type={entityType}");
            return null;
        }

        wblockDb.SaveAs(tempFile, DwgVersion.Current);

        if (!File.Exists(tempFile))
        {
            Log(
                $"SerializeEntityToDwgBytes failed: temp DWG not created " +
                $"for id={entityId}, path='{tempFile}'");
            return null;
        }

        byte[] bytes = File.ReadAllBytes(tempFile);
        Log(
            $"SerializeEntityToDwgBytes success: id={entityId}, " +
            $"type={entityType}, bytes={bytes.Length}");

        return bytes;
    }
    catch (Exception ex)
    {
        Log(
            $"SerializeEntityToDwgBytes exception: id={entityId}, " +
            $"handle={entityHandle}, type={entityType}, ex={ex}");
        return null;
    }
    finally
    {
        if (wblockDb != null)
        {
            try
            {
                wblockDb.Dispose();
            }
            catch (Exception ex)
            {
                Log(
                    $"SerializeEntityToDwgBytes dispose warning: " +
                    $"id={entityId}, ex={ex.Message}");
            }
        }

        try
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
        catch (Exception ex)
        {
            Log(
                $"SerializeEntityToDwgBytes temp delete warning: " +
                $"path='{tempFile}', ex={ex.Message}");
        }
    }
}

        /// <summary>
        /// If geometry_data is in the new dwg-blob format, decode and return
        /// the raw .dwg bytes. Otherwise return null and let the caller fall
        /// back to the bbox-based placeholder.
        /// </summary>
        public static byte[] TryGetDwgBytes(string geometryData)
        {
            if (string.IsNullOrEmpty(geometryData))
            {
                Log("TryGetDwgBytes: no geometry data present.");
                return null;
            }

            if (!geometryData.StartsWith(DwgFormatPrefix, StringComparison.Ordinal))
            {
                Log("TryGetDwgBytes: geometry data is legacy/non-DWG format.");
                return null;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(
                    geometryData.Substring(DwgFormatPrefix.Length).Trim());

                Log($"TryGetDwgBytes success: bytes={bytes.Length}");
                return bytes;
            }
            catch (Exception ex)
            {
                Log($"TryGetDwgBytes failed: {ex.Message}");
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
        ///
        /// Implementation note: instead of trusting <c>IdPair.IsCloned</c>
        /// (which behaves inconsistently across entity types - LWPOLYLINE
        /// in particular has been observed to skip the flag, dropping the
        /// real entity from the result and leaving callers with only a
        /// fallback bounding rectangle), we snapshot the target modelspace
        /// before the clone call and diff against it afterwards. Any entity
        /// that wasn't there before is by definition a clone we just made.
        /// </summary>
        public static List<ObjectId> CloneGeometryIntoDb(
            byte[] dwgBytes,
            Database targetDb,
            Matrix3d xform,
            string targetLayer = null,
            int? targetColorIndex = null)
        {
            var clonedIds = new List<ObjectId>();
            if (dwgBytes == null || dwgBytes.Length == 0)
            {
                Log("CloneGeometryIntoDb aborted: dwgBytes is null/empty.");
                return clonedIds;
            }

            if (targetDb == null)
            {
                Log("CloneGeometryIntoDb aborted: targetDb is null.");
                return clonedIds;
            }

            string tempFile = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N") + ".dwg");

            try
            {
                Log(
                    $"CloneGeometryIntoDb start: bytes={dwgBytes.Length}, " +
                    $"temp='{tempFile}', targetLayer='{targetLayer ?? ""}', " +
                    $"targetColor={(targetColorIndex.HasValue ? targetColorIndex.Value.ToString() : "<null>")}");

                File.WriteAllBytes(tempFile, dwgBytes);

                if (!File.Exists(tempFile))
                {
                    Log(
                        $"CloneGeometryIntoDb failed: temp file was not written: '{tempFile}'");
                    return clonedIds;
                }

                using (var sideDb = new Database(false, true))
                {
                    Log($"CloneGeometryIntoDb reading side DB from '{tempFile}'");
                    sideDb.ReadDwgFile(tempFile, FileShare.ReadWrite, true, "");
                    sideDb.CloseInput(true);

                    // Collect modelspace entity ids in the side-db.
                    var sourceIds = new ObjectIdCollection();
                    using (var trS = sideDb.TransactionManager.StartTransaction())
                    {
                        var btS = (BlockTable)trS.GetObject(
                            sideDb.BlockTableId,
                            OpenMode.ForRead);
                        var msS = (BlockTableRecord)trS.GetObject(
                            btS[BlockTableRecord.ModelSpace],
                            OpenMode.ForRead);

                        foreach (ObjectId id in msS) sourceIds.Add(id);

                        Log(
                            $"CloneGeometryIntoDb source modelspace count: " +
                            $"{sourceIds.Count}");

                        int sampleCount = 0;
                        foreach (ObjectId id in msS)
                        {
                            if (sampleCount >= 10) break;
                            try
                            {
                                var ent = trS.GetObject(id, OpenMode.ForRead) as Entity;
                                Log(
                                    $"CloneGeometryIntoDb source entity[{sampleCount}]: " +
                                    $"id={id}, type={ent?.GetType().FullName ?? "<not-entity>"}");
                            }
                            catch (Exception ex)
                            {
                                Log(
                                    $"CloneGeometryIntoDb source entity read warning: " +
                                    $"id={id}, ex={ex.Message}");
                            }

                            sampleCount++;
                        }

                        trS.Commit();
                    }

                    if (sourceIds.Count == 0)
                    {
                        Log(
                            "CloneGeometryIntoDb abort: side DB modelspace contains no entities.");
                        return clonedIds;
                    }

                    // Snapshot the target modelspace BEFORE cloning so we can
                    // identify newly-added clones afterwards by simple set
                    // difference. This is what lets us reliably catch
                    // LWPOLYLINE / polyline / region clones whose IdPair flags
                    // don't behave the way the docs imply.
                    ObjectId targetMsId;
                    var preExisting = new HashSet<ObjectId>();
                    using (var trT = targetDb.TransactionManager.StartTransaction())
                    {
                        var btT = (BlockTable)trT.GetObject(
                            targetDb.BlockTableId,
                            OpenMode.ForRead);
                        targetMsId = btT[BlockTableRecord.ModelSpace];
                        var msT = (BlockTableRecord)trT.GetObject(
                            targetMsId,
                            OpenMode.ForRead);

                        foreach (ObjectId id in msT) preExisting.Add(id);

                        Log(
                            $"CloneGeometryIntoDb target pre-existing entity count: " +
                            $"{preExisting.Count}");

                        trT.Commit();
                    }

                    var idMap = new IdMapping();

                    Log(
                        $"CloneGeometryIntoDb WblockCloneObjects start: " +
                        $"sourceCount={sourceIds.Count}");

                    sideDb.WblockCloneObjects(
                        sourceIds,
                        targetMsId,
                        idMap,
                        DuplicateRecordCloning.Replace,
                        false);

                    Log("CloneGeometryIntoDb WblockCloneObjects completed.");

                    // Walk the target modelspace and treat anything that
                    // wasn't there before as a freshly-added clone. Apply
                    // the transform + layer/color so it lands at the layout
                    // position with the right styling. Pre-existing entities
                    // (e.g. titleblock geometry from a template) are
                    // untouched because they're in preExisting.
                    using (var trT = targetDb.TransactionManager.StartTransaction())
                    {
                        var msT = (BlockTableRecord)trT.GetObject(
                            targetMsId,
                            OpenMode.ForRead);

                        foreach (ObjectId id in msT)
                        {
                            if (preExisting.Contains(id)) continue;

                            var clone = trT.GetObject(id, OpenMode.ForWrite) as Entity;
                            if (clone == null)
                            {
                                Log(
                                    $"CloneGeometryIntoDb warning: newly-added object " +
                                    $"is not an Entity: id={id}");
                                continue;
                            }

                            try
                            {
                                clone.TransformBy(xform);
                            }
                            catch (Exception ex)
                            {
                                Log(
                                    $"CloneGeometryIntoDb transform warning: " +
                                    $"id={id}, type={clone.GetType().FullName}, ex={ex.Message}");
                            }

                            if (!string.IsNullOrEmpty(targetLayer))
                            {
                                try
                                {
                                    clone.Layer = targetLayer;
                                }
                                catch (Exception ex)
                                {
                                    Log(
                                        $"CloneGeometryIntoDb layer warning: " +
                                        $"id={id}, type={clone.GetType().FullName}, ex={ex.Message}");
                                }
                            }

                            if (targetColorIndex.HasValue)
                            {
                                try
                                {
                                    clone.ColorIndex = targetColorIndex.Value;
                                }
                                catch (Exception ex)
                                {
                                    Log(
                                        $"CloneGeometryIntoDb color warning: " +
                                        $"id={id}, type={clone.GetType().FullName}, ex={ex.Message}");
                                }
                            }

                            clonedIds.Add(id);

                            Log(
                                $"CloneGeometryIntoDb cloned entity: " +
                                $"id={id}, type={clone.GetType().FullName}");
                        }

                        trT.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"CloneGeometryIntoDb exception: {ex}");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
                catch (Exception ex)
                {
                    Log(
                        $"CloneGeometryIntoDb temp delete warning: " +
                        $"path='{tempFile}', ex={ex.Message}");
                }
            }

            Log($"CloneGeometryIntoDb result: clonedIds.Count={clonedIds.Count}");
            return clonedIds;
        }

        /// <summary>
        /// Recreate a part from its stored geometry at
        /// <paramref name="insertionPoint"/>.
        /// New format: clones the original entity exactly (curves preserved).
        /// Legacy format (or missing geometry): falls back to a 3D placeholder
        /// box sized to the stored bounding box. Returns the primary clone's
        /// ObjectId, or ObjectId.Null on failure.
        /// </summary>
        public static ObjectId CreateEntityFromPart(
            DrawingPart part,
            Point3d insertionPoint)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Log("CreateEntityFromPart aborted: no active document.");
                return ObjectId.Null;
            }

            var db = doc.Database;

            Log(
                $"CreateEntityFromPart start: partName='{part?.PartName ?? ""}', " +
                $"insertion=({insertionPoint.X:F3}, {insertionPoint.Y:F3}, {insertionPoint.Z:F3})");

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
                    ObjectId firstId = clones.Count > 0
                        ? clones[0]
                        : ObjectId.Null;

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

                        Log(
                            $"CreateEntityFromPart success: partName='{part?.PartName ?? ""}', " +
                            $"cloneCount={clones.Count}, firstId={firstId}");

                        return firstId;
                    }

                    Log(
                        $"CreateEntityFromPart clone path produced no entities; " +
                        $"falling back to placeholder. partName='{part?.PartName ?? ""}'");
                }
            }
            else
            {
                Log(
                    $"CreateEntityFromPart: no usable DWG bytes or bbox min missing; " +
                    $"falling back to placeholder. partName='{part?.PartName ?? ""}'");
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

        private static ObjectId CreateLegacyPlaceholderBox(
            DrawingPart part,
            Point3d insertionPoint,
            Database db,
            AcadDocument doc)
        {
            if (!part.BBoxMinX.HasValue)
            {
                Log(
                    $"CreateLegacyPlaceholderBox aborted: no bbox available for " +
                    $"partName='{part?.PartName ?? ""}'");
                return ObjectId.Null;
            }

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var ms = tr.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite) as BlockTableRecord;

                double width = Math.Max(0.1, (part.BBoxMaxX ?? 0) - (part.BBoxMinX ?? 0));
                double depth = Math.Max(0.1, (part.BBoxMaxY ?? 0) - (part.BBoxMinY ?? 0));
                double height = Math.Max(0.1, (part.BBoxMaxZ ?? 0) - (part.BBoxMinZ ?? 0));

                var solid = new Solid3d();
                solid.CreateBox(width, depth, height);

                var center = new Point3d(
                    insertionPoint.X,
                    insertionPoint.Y,
                    insertionPoint.Z + height / 2);
                solid.TransformBy(Matrix3d.Displacement(center.GetAsVector()));

                solid.Layer = part.LayerName ?? "0";
                if (part.ColorIndex != 256) solid.ColorIndex = part.ColorIndex;

                ObjectId id = ms.AppendEntity(solid);
                tr.AddNewlyCreatedDBObject(solid, true);
                SetPartXData(solid, part, tr, db);
                tr.Commit();

                Log(
                    $"CreateLegacyPlaceholderBox created placeholder: " +
                    $"partName='{part?.PartName ?? ""}', id={id}, " +
                    $"size=({width:F3}, {depth:F3}, {height:F3})");

                return id;
            }
        }

        // ====================================================================
        // XDATA
        // ====================================================================

        public static void SetPartXData(
            Entity entity,
            DrawingPart part,
            Transaction tr,
            Database db)
        {
            var regAppTable = tr.GetObject(
                db.RegAppTableId,
                OpenMode.ForWrite) as RegAppTable;

            if (!regAppTable.Has(Commands.AppName))
            {
                var regApp = new RegAppTableRecord { Name = Commands.AppName };
                regAppTable.Add(regApp);
                tr.AddNewlyCreatedDBObject(regApp, true);
            }

            // Preserve ProfileName if previously stamped
            // (e.g. by MCInsertProfile).
            string profileName = GetProfileName(entity);

            var xdata = new ResultBuffer(
                new TypedValue(
                    (int)DxfCode.ExtendedDataRegAppName,
                    Commands.AppName),
                new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    "PartName"),
                new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    part.PartName ?? ""),
                new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    "PartId"),
                new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    part.Id.ToString()),
                new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    "ParentPartId"),
                new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    part.ParentPartId?.ToString() ?? ""),
                new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    "IsOriginal"),
                new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    part.IsOriginal.ToString()),
                new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    "ProfileName"),
                new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    profileName)
            );

            entity.XData = xdata;
        }

        public static string GetProfileName(Entity entity)
        {
            var xdata = entity.GetXDataForApplication(Commands.AppName);
            if (xdata == null) return "";

            var values = xdata.AsArray();
            for (int i = 1; i + 1 < values.Length; i += 2)
            {
                if (values[i].Value.ToString() == "ProfileName")
                    return values[i + 1].Value.ToString();
            }

            return "";
        }

        /// <summary>
        /// Stamps (or updates) the "ProfileName" key in the entity's XData,
        /// preserving any other existing key-value pairs under MC_CONSTRUCTOR.
        /// </summary>
        public static void SetProfileXData(
            Entity entity,
            string profileName,
            Transaction tr,
            Database db)
        {
            var regAppTable = tr.GetObject(
                db.RegAppTableId,
                OpenMode.ForWrite) as RegAppTable;

            if (!regAppTable.Has(Commands.AppName))
            {
                var regApp = new RegAppTableRecord { Name = Commands.AppName };
                regAppTable.Add(regApp);
                tr.AddNewlyCreatedDBObject(regApp, true);
            }

            var vals = new List<TypedValue>();
            vals.Add(new TypedValue(
                (int)DxfCode.ExtendedDataRegAppName,
                Commands.AppName));

            bool hasProfileKey = false;
            var existing = entity.GetXDataForApplication(Commands.AppName);
            if (existing != null)
            {
                var arr = existing.AsArray();
                for (int i = 1; i + 1 < arr.Length; i += 2)
                {
                    if (arr[i].Value.ToString() == "ProfileName")
                    {
                        hasProfileKey = true;
                        vals.Add(new TypedValue(
                            (int)DxfCode.ExtendedDataAsciiString,
                            "ProfileName"));
                        vals.Add(new TypedValue(
                            (int)DxfCode.ExtendedDataAsciiString,
                            profileName));
                    }
                    else
                    {
                        vals.Add(arr[i]);
                        vals.Add(arr[i + 1]);
                    }
                }
            }

            if (!hasProfileKey)
            {
                vals.Add(new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    "ProfileName"));
                vals.Add(new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    profileName));
            }

            entity.XData = new ResultBuffer(vals.ToArray());
        }

        /// <summary>
        /// Updates specific XData keys while preserving all other existing
        /// key-value pairs. Keys in <paramref name="updates"/> that don't yet
        /// exist in the XData are appended. Use an empty string value to clear
        /// a key.
        /// </summary>
        public static void MergeXData(
            Entity entity,
            System.Collections.Generic.Dictionary<string, string> updates,
            Transaction tr,
            Database db)
        {
            var regAppTable = tr.GetObject(
                db.RegAppTableId,
                OpenMode.ForWrite) as RegAppTable;

            if (!regAppTable.Has(Commands.AppName))
            {
                var regApp = new RegAppTableRecord { Name = Commands.AppName };
                regAppTable.Add(regApp);
                tr.AddNewlyCreatedDBObject(regApp, true);
            }

            var vals = new List<TypedValue>();
            vals.Add(new TypedValue(
                (int)DxfCode.ExtendedDataRegAppName,
                Commands.AppName));

            var handled = new System.Collections.Generic.HashSet<string>();
            var existing = entity.GetXDataForApplication(Commands.AppName);
            if (existing != null)
            {
                var arr = existing.AsArray();
                for (int i = 1; i + 1 < arr.Length; i += 2)
                {
                    string key = arr[i].Value.ToString();
                    string newVal;
                    if (updates.TryGetValue(key, out newVal))
                    {
                        vals.Add(new TypedValue(
                            (int)DxfCode.ExtendedDataAsciiString,
                            key));
                        vals.Add(new TypedValue(
                            (int)DxfCode.ExtendedDataAsciiString,
                            newVal));
                        handled.Add(key);
                    }
                    else
                    {
                        vals.Add(arr[i]);
                        vals.Add(arr[i + 1]);
                    }
                }
            }

            foreach (var kv in updates)
            {
                if (!handled.Contains(kv.Key))
                {
                    vals.Add(new TypedValue(
                        (int)DxfCode.ExtendedDataAsciiString,
                        kv.Key));
                    vals.Add(new TypedValue(
                        (int)DxfCode.ExtendedDataAsciiString,
                        kv.Value));
                }
            }

            entity.XData = new ResultBuffer(vals.ToArray());
        }

        public static (string partName, Guid? partId, Guid? parentPartId, bool isOriginal)
            GetPartXData(Entity entity)
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
            if (doc == null)
            {
                Log("UpdateSolidFromPart aborted: no active document.");
                return false;
            }

            var db = doc.Database;

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(entityId, OpenMode.ForWrite) as Entity;
                if (entity == null)
                {
                    Log($"UpdateSolidFromPart failed: id={entityId} is not an Entity.");
                    return false;
                }

                if (!string.IsNullOrEmpty(part.LayerName)) entity.Layer = part.LayerName;
                if (part.ColorIndex != 256) entity.ColorIndex = part.ColorIndex;

                tr.Commit();

                Log(
                    $"UpdateSolidFromPart success: id={entityId}, " +
                    $"partName='{part?.PartName ?? ""}'");

                return true;
            }
        }
    }
}
