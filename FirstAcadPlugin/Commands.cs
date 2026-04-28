using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.IO;

namespace FirstAcadPlugin
{
    public class Commands
    {
        public const string AppName = "MC_CONSTRUCTOR";

        [CommandMethod("MC_GREET")]
        public void Greet()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc.Editor.WriteMessage("\nHello World!");
        }

        [CommandMethod("MC_ADD_PART_METADATA")]
        public void AddPartMetadata()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var editor = doc.Editor;

            var options = new PromptEntityOptions("\nSelect a 3D solid to add metadata: ");
            options.SetRejectMessage("\nMust be a 3D solid.");
            options.AddAllowedClass(typeof(Solid3d), true);

            var result = editor.GetEntity(options);
            if (result.Status != PromptStatus.OK) { editor.WriteMessage("\nCommand cancelled."); return; }

            var dialog = new PartMetadataDialog();
            if (Application.ShowModalWindow(dialog) != true || string.IsNullOrEmpty(dialog.PartName))
            { editor.WriteMessage("\nCommand cancelled."); return; }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(result.ObjectId, OpenMode.ForWrite) as Entity;
                if (entity == null) { editor.WriteMessage("\nError accessing object."); return; }

                RegisterApp(db, tr);

                var xdata = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, "PartName"),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, dialog.PartName)
                );

                entity.XData = xdata;
                tr.Commit();

                editor.WriteMessage($"\nMetadata added: {dialog.PartName}");
            }
        }

        private void RegisterApp(Database db, Transaction tr)
        {
            var regAppTable = tr.GetObject(db.RegAppTableId, OpenMode.ForWrite) as RegAppTable;
            if (!regAppTable.Has(AppName))
            {
                var regApp = new RegAppTableRecord { Name = AppName };
                regAppTable.Add(regApp);
                tr.AddNewlyCreatedDBObject(regApp, true);
            }
        }

        [CommandMethod("MC_VIEW_PART_METADATA")]
        public void ViewPartMetadata()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            var options = new PromptEntityOptions("\nSelect a 3D solid: ");
            options.SetRejectMessage("\nMust be a 3D solid.");
            options.AddAllowedClass(typeof(Solid3d), true);

            var result = editor.GetEntity(options);
            if (result.Status != PromptStatus.OK) return;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(result.ObjectId, OpenMode.ForRead) as Entity;
                var xdata = entity?.GetXDataForApplication(AppName);

                if (xdata == null)
                { editor.WriteMessage("\nNo metadata found."); return; }

                editor.WriteMessage("\n--- Part Metadata ---");
                var values = xdata.AsArray();
                for (int i = 1; i < values.Length - 1; i += 2)
                    editor.WriteMessage($"\n  {values[i].Value}: {values[i + 1].Value}");
                editor.WriteMessage("\n---------------------");
            }
        }

        [CommandMethod("MC_SHOW_METADATA_PALETTE")]
        public void ShowMetadataPalette() => MetadataPalette.Toggle();

        [CommandMethod("MC_OPEN_PROJECT")]
        public void OpenProject()
        {
            var editor = Application.DocumentManager.MdiActiveDocument.Editor;
            var dialog = new ProjectDialog();

            if (Application.ShowModalWindow(dialog) == true && dialog.SelectedProject != null)
            {
                DatabaseService.SetCurrentProject(dialog.SelectedProject);
                editor.WriteMessage($"\nProject opened: {dialog.SelectedProject.Name}");
                MetadataPalette.RefreshProjectInfo();
            }
        }

        [CommandMethod("MC_CONFIG_DATABASE")]
        public void ConfigDatabase()
        {
            var dialog = new DatabaseConfigDialog();
            if (Application.ShowModalWindow(dialog) == true)
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nDatabase config saved.");
        }

        [CommandMethod("MC_SAVE_PARTS")]
        public void SaveParts()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            if (DatabaseService.CurrentProject == null)
            { editor.WriteMessage("\nNo project open. Use MC_OPEN_PROJECT first."); return; }

            try
            {
                editor.WriteMessage("\nSaving parts with full geometry data...");
                int count = SaveAllPartsWithGeometry();
                editor.WriteMessage($"\nSaved {count} part(s) to project: {DatabaseService.CurrentProject.Name}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nError: {ex.Message}");
            }
        }

        /// <summary>
        /// Save all parts with complete geometry data.
        /// </summary>
        private int SaveAllPartsWithGeometry()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            int savedCount = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                foreach (ObjectId objId in ms)
                {
                    var entity = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                    if (!(entity is Solid3d solid)) continue;

                    var xdata = entity.GetXDataForApplication(AppName);
                    if (xdata == null) continue;

                    // Get part name from XData
                    string partName = "";
                    var values = xdata.AsArray();
                    for (int i = 1; i < values.Length - 1; i += 2)
                    {
                        if (values[i].Value.ToString() == "PartName")
                            partName = values[i + 1].Value.ToString();
                    }

                    if (string.IsNullOrEmpty(partName)) continue;

                    // Extract and save full part data
                    var part = PartGeometryHelper.ExtractPartData(objId, partName);
                    if (part != null)
                    {
                        DatabaseService.SavePart(part);
                        savedCount++;
                    }
                }

                tr.Commit();
            }

            return savedCount;
        }

        [CommandMethod("MC_PROJECT_STATUS")]
        public void ProjectStatus()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            editor.WriteMessage("\n========== MC Constructor Status ==========");

            if (DatabaseService.CurrentProject != null)
            {
                editor.WriteMessage($"\n  Project: {DatabaseService.CurrentProject.Name}");

                // Get parts count from database
                var parts = DatabaseService.GetOriginalParts();
                editor.WriteMessage($"\n  Parts in Database: {parts.Count}");
            }
            else
            {
                editor.WriteMessage("\n  No project open");
            }

            editor.WriteMessage($"\n  Drawing: {Path.GetFileName(doc.Name)}");

            var stats = PartPropertiesManager.GetPartStatistics();
            editor.WriteMessage($"\n  3D Solids: {stats.totalSolids}");
            editor.WriteMessage($"\n  With Metadata: {stats.withMetadata}");
            editor.WriteMessage("\n============================================");
        }

        [CommandMethod("MC_LIST_PARTS")]
        public void ListParts()
        {
            var editor = Application.DocumentManager.MdiActiveDocument.Editor;
            var parts = PartPropertiesManager.GetAllPartsInDrawing();

            if (parts.Count == 0)
            { editor.WriteMessage("\nNo parts found. Use MC_ADD_PART_METADATA first."); return; }

            editor.WriteMessage($"\n========== Parts ({parts.Count}) ==========");
            for (int i = 0; i < parts.Count; i++)
                editor.WriteMessage($"\n  {i + 1}. {parts[i].PartName}");
            editor.WriteMessage("\n==========================================");
        }

        // ==================== NEW PART REFERENCE COMMANDS ====================

        /// <summary>
        /// MC_INSERT_PART - Insert a part from the database into the current drawing.
        /// Works like xref but the part is editable.
        /// </summary>
        [CommandMethod("MC_INSERT_PART")]
        public void InsertPart()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            if (DatabaseService.CurrentProject == null)
            { editor.WriteMessage("\nNo project open. Use MC_OPEN_PROJECT first."); return; }

            // Get available parts from database
            var availableParts = DatabaseService.GetOriginalParts();

            if (availableParts.Count == 0)
            { editor.WriteMessage("\nNo parts found in database. Save parts first."); return; }

            // Show part selection dialog
            var dialog = new InsertPartDialog(availableParts);
            if (Application.ShowModalWindow(dialog) != true || dialog.SelectedPart == null)
            { editor.WriteMessage("\nCommand cancelled."); return; }

            var partToInsert = dialog.SelectedPart;

            // Get insertion point
            var pointResult = editor.GetPoint("\nSpecify insertion point: ");
            if (pointResult.Status != PromptStatus.OK)
            { editor.WriteMessage("\nCommand cancelled."); return; }

            try
            {
                // Create the solid from part data
                var newSolidId = PartGeometryHelper.CreateSolidFromPart(partToInsert, pointResult.Value);

                if (newSolidId != ObjectId.Null)
                {
                    // Save as a reference in database
                    var newPart = PartGeometryHelper.ExtractPartData(newSolidId, partToInsert.PartName);
                    if (newPart != null)
                    {
                        newPart.ParentPartId = partToInsert.Id;
                        newPart.IsOriginal = false;
                        DatabaseService.SavePart(newPart);
                    }

                    editor.WriteMessage($"\nInserted part: {partToInsert.PartName}");
                    editor.WriteMessage("\nThis is a reference. Use MC_OVERRIDE_PART to make local changes.");
                }
                else
                {
                    editor.WriteMessage("\nFailed to create part geometry.");
                }
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nError: {ex.Message}");
            }
        }

        /// <summary>
        /// MC_OVERRIDE_PART - Override a part reference with local changes.
        /// Changes will sync back to original when MC_UPDATE_ALL_PARTS is run.
        /// </summary>
        [CommandMethod("MC_OVERRIDE_PART")]
        public void OverridePart()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            if (DatabaseService.CurrentProject == null)
            { editor.WriteMessage("\nNo project open."); return; }

            // Select the part to override
            var options = new PromptEntityOptions("\nSelect a part to override: ");
            options.SetRejectMessage("\nMust be a 3D solid.");
            options.AddAllowedClass(typeof(Solid3d), true);

            var result = editor.GetEntity(options);
            if (result.Status != PromptStatus.OK) return;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var solid = tr.GetObject(result.ObjectId, OpenMode.ForRead) as Solid3d;
                if (solid == null) return;

                // Get part info from XData
                var (partName, partId, parentPartId, isOriginal) = PartGeometryHelper.GetPartXData(solid);

                if (string.IsNullOrEmpty(partName))
                { editor.WriteMessage("\nThis object has no part metadata."); return; }

                if (isOriginal)
                { editor.WriteMessage("\nThis is an original part. Changes are saved directly."); return; }

                if (!partId.HasValue)
                { editor.WriteMessage("\nPart ID not found. Save the part first."); return; }

                // Confirm override
                var confirmResult = editor.GetKeywords(
                    new PromptKeywordOptions($"\nOverride '{partName}'? Changes will sync to original. [Yes/No]", "Yes No")
                    { AllowNone = false }
                );

                if (confirmResult.Status != PromptStatus.OK || confirmResult.StringResult != "Yes")
                { editor.WriteMessage("\nOverride cancelled."); return; }

                tr.Commit();
            }

            // Extract updated geometry and mark as override
            var updatedPart = PartGeometryHelper.ExtractPartData(result.ObjectId, "");
            if (updatedPart == null)
            { editor.WriteMessage("\nFailed to extract part data."); return; }

            try
            {
                // Get the part ID from XData
                Guid? partId = null;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var solid = tr.GetObject(result.ObjectId, OpenMode.ForRead) as Solid3d;
                    (_, partId, _, _) = PartGeometryHelper.GetPartXData(solid);
                    tr.Commit();
                }

                if (partId.HasValue)
                {
                    DatabaseService.OverridePart(partId.Value, updatedPart);
                    editor.WriteMessage("\nPart marked as overridden.");
                    editor.WriteMessage("\nRun MC_UPDATE_ALL_PARTS in the original drawing to sync changes.");
                }
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nError: {ex.Message}");
            }
        }

        /// <summary>
        /// MC_UPDATE_ALL_PARTS - Update all parts in the drawing from database.
        /// Applies changes from overridden references.
        /// </summary>
        [CommandMethod("MC_UPDATE_ALL_PARTS")]
        public void UpdateAllParts()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            if (DatabaseService.CurrentProject == null)
            { editor.WriteMessage("\nNo project open."); return; }

            // First, apply any overrides to originals
            var overriddenParts = DatabaseService.GetOverriddenParts();
            int appliedOverrides = 0;

            foreach (var part in overriddenParts)
            {
                if (part.ParentPartId.HasValue)
                {
                    try
                    {
                        DatabaseService.ApplyOverrideToOriginal(part.Id);
                        appliedOverrides++;
                    }
                    catch { }
                }
            }

            if (appliedOverrides > 0)
                editor.WriteMessage($"\nApplied {appliedOverrides} override(s) to original parts.");

            // Now update parts in this drawing
            int updatedCount = 0;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
                var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                foreach (ObjectId objId in ms)
                {
                    var entity = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                    if (!(entity is Solid3d)) continue;

                    var (partName, partId, parentPartId, isOriginal) = PartGeometryHelper.GetPartXData(entity);

                    if (!parentPartId.HasValue) continue; // Not a reference

                    // Get the latest version from database
                    var latestPart = DatabaseService.GetPartById(parentPartId.Value);
                    if (latestPart == null) continue;

                    // Update the solid
                    if (PartGeometryHelper.UpdateSolidFromPart(objId, latestPart))
                        updatedCount++;
                }

                tr.Commit();
            }

            editor.WriteMessage($"\nUpdated {updatedCount} part reference(s) in this drawing.");
        }

        /// <summary>
        /// MC_LIST_DB_PARTS - List all parts in the database for current project.
        /// </summary>
        [CommandMethod("MC_LIST_DB_PARTS")]
        public void ListDatabaseParts()
        {
            var editor = Application.DocumentManager.MdiActiveDocument.Editor;

            if (DatabaseService.CurrentProject == null)
            { editor.WriteMessage("\nNo project open."); return; }

            var parts = DatabaseService.GetOriginalParts();

            editor.WriteMessage($"\n========== Parts in Database ({parts.Count}) ==========");

            foreach (var part in parts)
            {
                string status = part.IsOverride ? " [OVERRIDDEN]" : "";
                editor.WriteMessage($"\n  - {part.PartName}{status}");
                editor.WriteMessage($"\n      Source: {part.SourceDrawingName}");
                editor.WriteMessage($"\n      ID: {part.Id}");
            }

            editor.WriteMessage("\n======================================================");
        }
    }
}
