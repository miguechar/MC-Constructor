using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;

// Alias to avoid conflicts
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Manages part properties and synchronizes with the database.
    /// This class provides methods to read and write part metadata,
    /// and sync with the PostgreSQL database.
    /// </summary>
    public static class PartPropertiesManager
    {
        /// <summary>
        /// Get all parts with metadata in the current drawing.
        /// </summary>
        public static List<DrawingPart> GetAllPartsInDrawing()
        {
            var parts = new List<DrawingPart>();
            var doc = AcadApp.DocumentManager.MdiActiveDocument;

            if (doc == null)
                return parts;

            var db = doc.Database;
            string drawingName = System.IO.Path.GetFileName(doc.Name);

            using (var transaction = db.TransactionManager.StartTransaction())
            {
                var blockTable = transaction.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var modelSpace = transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                foreach (ObjectId objId in modelSpace)
                {
                    var entity = transaction.GetObject(objId, OpenMode.ForRead) as Entity;

                    // Only process 3D solids
                    if (!(entity is Solid3d))
                        continue;

                    // Check if it has our XData
                    var xdata = entity.GetXDataForApplication(Commands.AppName);

                    if (xdata == null)
                        continue;

                    // Parse the XData
                    var values = xdata.AsArray();
                    string partName = "";

                    for (int i = 1; i < values.Length - 1; i += 2)
                    {
                        string label = values[i].Value.ToString();
                        string value = values[i + 1].Value.ToString();

                        if (label == "PartName")
                            partName = value;
                    }

                    if (!string.IsNullOrEmpty(partName))
                    {
                        parts.Add(new DrawingPart
                        {
                            DrawingName = drawingName,
                            PartName = partName,
                            ObjectHandle = entity.Handle.ToString()
                        });
                    }
                }

                transaction.Commit();
            }

            return parts;
        }

        /// <summary>
        /// Get the metadata for a specific object.
        /// </summary>
        public static string GetPartName(ObjectId objectId)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return null;

            using (var transaction = doc.Database.TransactionManager.StartTransaction())
            {
                var entity = transaction.GetObject(objectId, OpenMode.ForRead) as Entity;

                if (entity == null)
                    return null;

                var xdata = entity.GetXDataForApplication(Commands.AppName);

                if (xdata == null)
                    return null;

                var values = xdata.AsArray();

                for (int i = 1; i < values.Length - 1; i += 2)
                {
                    string label = values[i].Value.ToString();
                    string value = values[i + 1].Value.ToString();

                    if (label == "PartName")
                        return value;
                }

                transaction.Commit();
            }

            return null;
        }

        /// <summary>
        /// Set the part name for a specific object.
        /// </summary>
        public static bool SetPartName(ObjectId objectId, string partName)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return false;

            using (var docLock = doc.LockDocument())
            using (var transaction = doc.Database.TransactionManager.StartTransaction())
            {
                var entity = transaction.GetObject(objectId, OpenMode.ForWrite) as Entity;

                if (entity == null || !(entity is Solid3d))
                    return false;

                // Register app name
                var regAppTable = transaction.GetObject(doc.Database.RegAppTableId, OpenMode.ForWrite) as RegAppTable;
                if (!regAppTable.Has(Commands.AppName))
                {
                    var regApp = new RegAppTableRecord();
                    regApp.Name = Commands.AppName;
                    regAppTable.Add(regApp);
                    transaction.AddNewlyCreatedDBObject(regApp, true);
                }

                // Create XData
                var xdata = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, Commands.AppName),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, "PartName"),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, partName)
                );

                entity.XData = xdata;
                transaction.Commit();

                return true;
            }
        }

        /// <summary>
        /// Save all parts in the current drawing to the database.
        /// </summary>
        public static int SaveAllPartsToDatabase()
        {
            if (DatabaseService.CurrentProject == null)
                throw new InvalidOperationException("No project is currently open. Use MC_OPEN_PROJECT first.");

            var parts = GetAllPartsInDrawing();

            if (parts.Count == 0)
                return 0;

            return DatabaseService.SaveAllDrawingParts(parts);
        }

        /// <summary>
        /// Get statistics about parts in the current drawing.
        /// </summary>
        public static (int totalSolids, int withMetadata, int withoutMetadata) GetPartStatistics()
        {
            int totalSolids = 0;
            int withMetadata = 0;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return (0, 0, 0);

            var db = doc.Database;

            using (var transaction = db.TransactionManager.StartTransaction())
            {
                var blockTable = transaction.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var modelSpace = transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                foreach (ObjectId objId in modelSpace)
                {
                    var entity = transaction.GetObject(objId, OpenMode.ForRead) as Entity;

                    if (entity is Solid3d)
                    {
                        totalSolids++;

                        var xdata = entity.GetXDataForApplication(Commands.AppName);
                        if (xdata != null)
                            withMetadata++;
                    }
                }

                transaction.Commit();
            }

            return (totalSolids, withMetadata, totalSolids - withMetadata);
        }
    }
}
