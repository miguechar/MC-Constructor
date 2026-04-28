using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Contains all the AutoCAD commands for the MC Constructor plugin.
    /// Each method with [CommandMethod] becomes a command you can type in AutoCAD.
    /// </summary>
    public class Commands
    {
        // This is our unique application name for XData
        // XData is how AutoCAD stores custom data on objects
        public const string AppName = "MC_CONSTRUCTOR";

        /// <summary>
        /// The MC_GREET command - writes "Hello World!" to the AutoCAD console.
        /// </summary>
        [CommandMethod("MC_GREET")]
        public void Greet()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            editor.WriteMessage("\nHello World!");
        }

        /// <summary>
        /// The MC_ADD_PART_METADATA command.
        /// Allows you to select a 3D solid and add metadata (Part Name) to it.
        /// The metadata is stored as XData directly on the object.
        /// </summary>
        [CommandMethod("MC_ADD_PART_METADATA")]
        public void AddPartMetadata()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var editor = doc.Editor;

            // Step 1: Ask the user to select a 3D solid
            var options = new PromptEntityOptions("\nSelect a 3D solid to add metadata: ");
            options.SetRejectMessage("\nMust be a 3D solid.");
            options.AddAllowedClass(typeof(Solid3d), true);

            var result = editor.GetEntity(options);

            if (result.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nCommand cancelled.");
                return;
            }

            // Step 2: Show the metadata dialog
            var dialog = new PartMetadataDialog();
            var dialogResult = Application.ShowModalWindow(dialog);

            if (dialogResult != true || string.IsNullOrEmpty(dialog.PartName))
            {
                editor.WriteMessage("\nCommand cancelled.");
                return;
            }

            // Step 3: Store the metadata on the selected object
            using (var transaction = db.TransactionManager.StartTransaction())
            {
                // Get the selected entity
                var entity = transaction.GetObject(result.ObjectId, OpenMode.ForWrite) as Entity;

                if (entity == null)
                {
                    editor.WriteMessage("\nError: Could not access the selected object.");
                    return;
                }

                // Register our application name for XData (required before using it)
                RegisterApp(db, transaction);

                // Create the XData to store
                // XData is a list of TypedValue pairs
                var xdata = new ResultBuffer(
                    // The application name (required as first item)
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                    // Store "PartName" as a label
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, "PartName"),
                    // Store the actual part name value
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, dialog.PartName)
                );

                // Attach the XData to the entity
                entity.XData = xdata;

                // Commit the transaction (save changes)
                transaction.Commit();

                editor.WriteMessage($"\nMetadata added successfully!");
                editor.WriteMessage($"\n  Part Name: {dialog.PartName}");
            }
        }

        /// <summary>
        /// Registers our application name in the drawing's RegApp table.
        /// This is required before we can use XData with our app name.
        /// </summary>
        private void RegisterApp(Database db, Transaction transaction)
        {
            var regAppTable = transaction.GetObject(db.RegAppTableId, OpenMode.ForWrite) as RegAppTable;

            // Only add if it doesn't already exist
            if (!regAppTable.Has(AppName))
            {
                var regApp = new RegAppTableRecord();
                regApp.Name = AppName;
                regAppTable.Add(regApp);
                transaction.AddNewlyCreatedDBObject(regApp, true);
            }
        }

        /// <summary>
        /// The MC_VIEW_PART_METADATA command.
        /// Select a 3D solid to view its stored metadata.
        /// </summary>
        [CommandMethod("MC_VIEW_PART_METADATA")]
        public void ViewPartMetadata()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var editor = doc.Editor;

            // Ask the user to select a 3D solid
            var options = new PromptEntityOptions("\nSelect a 3D solid to view metadata: ");
            options.SetRejectMessage("\nMust be a 3D solid.");
            options.AddAllowedClass(typeof(Solid3d), true);

            var result = editor.GetEntity(options);

            if (result.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nCommand cancelled.");
                return;
            }

            using (var transaction = db.TransactionManager.StartTransaction())
            {
                var entity = transaction.GetObject(result.ObjectId, OpenMode.ForRead) as Entity;

                if (entity == null)
                {
                    editor.WriteMessage("\nError: Could not access the selected object.");
                    return;
                }

                // Get XData for our application
                var xdata = entity.GetXDataForApplication(AppName);

                if (xdata == null)
                {
                    editor.WriteMessage("\nNo metadata found on this object.");
                    editor.WriteMessage("\nUse MC_ADD_PART_METADATA to add metadata.");
                    return;
                }

                // Parse and display the XData
                editor.WriteMessage("\n--- Part Metadata ---");

                var values = xdata.AsArray();
                for (int i = 1; i < values.Length - 1; i += 2)
                {
                    string label = values[i].Value.ToString();
                    string value = values[i + 1].Value.ToString();
                    editor.WriteMessage($"\n  {label}: {value}");
                }

                editor.WriteMessage("\n---------------------");
            }
        }

        /// <summary>
        /// The MC_SHOW_METADATA_PALETTE command.
        /// Shows or hides the metadata palette.
        /// </summary>
        [CommandMethod("MC_SHOW_METADATA_PALETTE")]
        public void ShowMetadataPalette()
        {
            MetadataPalette.Toggle();
        }

        // ========== DATABASE COMMANDS ==========

        /// <summary>
        /// The MC_OPEN_PROJECT command.
        /// Opens a dialog to connect to the database and select a project.
        /// </summary>
        [CommandMethod("MC_OPEN_PROJECT")]
        public void OpenProject()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            var dialog = new ProjectDialog();
            var result = Application.ShowModalWindow(dialog);

            if (result == true && dialog.SelectedProject != null)
            {
                DatabaseService.SetCurrentProject(dialog.SelectedProject);
                editor.WriteMessage($"\nProject opened: {dialog.SelectedProject.Name}");
                editor.WriteMessage($"\nProject ID: {dialog.SelectedProject.Id}");

                // Update the palette to show the current project
                MetadataPalette.RefreshProjectInfo();
            }
            else
            {
                editor.WriteMessage("\nNo project selected.");
            }
        }

        /// <summary>
        /// The MC_CONFIG_DATABASE command.
        /// Opens a dialog to configure the database connection.
        /// </summary>
        [CommandMethod("MC_CONFIG_DATABASE")]
        public void ConfigDatabase()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            var dialog = new DatabaseConfigDialog();
            var result = Application.ShowModalWindow(dialog);

            if (result == true)
            {
                editor.WriteMessage("\nDatabase configuration saved.");
            }
        }

        /// <summary>
        /// The MC_SAVE_PARTS command.
        /// Saves all parts with metadata in the current drawing to the database.
        /// </summary>
        [CommandMethod("MC_SAVE_PARTS")]
        public void SaveParts()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            // Check if a project is open
            if (DatabaseService.CurrentProject == null)
            {
                editor.WriteMessage("\nNo project is currently open.");
                editor.WriteMessage("\nUse MC_OPEN_PROJECT to select a project first.");
                return;
            }

            try
            {
                // Get statistics first
                var stats = PartPropertiesManager.GetPartStatistics();
                editor.WriteMessage($"\n--- Drawing Statistics ---");
                editor.WriteMessage($"\n  Total 3D Solids: {stats.totalSolids}");
                editor.WriteMessage($"\n  With Metadata: {stats.withMetadata}");
                editor.WriteMessage($"\n  Without Metadata: {stats.withoutMetadata}");

                if (stats.withMetadata == 0)
                {
                    editor.WriteMessage("\nNo parts with metadata found to save.");
                    return;
                }

                // Save to database
                int savedCount = PartPropertiesManager.SaveAllPartsToDatabase();

                editor.WriteMessage($"\n--- Save Complete ---");
                editor.WriteMessage($"\nSaved {savedCount} part(s) to project: {DatabaseService.CurrentProject.Name}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nError saving parts: {ex.Message}");
            }
        }

        /// <summary>
        /// The MC_PROJECT_STATUS command.
        /// Shows the current project status and drawing statistics.
        /// </summary>
        [CommandMethod("MC_PROJECT_STATUS")]
        public void ProjectStatus()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            editor.WriteMessage("\n========== MC Constructor Status ==========");

            // Project info
            if (DatabaseService.CurrentProject != null)
            {
                editor.WriteMessage($"\n  Current Project: {DatabaseService.CurrentProject.Name}");
                editor.WriteMessage($"\n  Project ID: {DatabaseService.CurrentProject.Id}");
            }
            else
            {
                editor.WriteMessage("\n  No project open (use MC_OPEN_PROJECT)");
            }

            // Drawing info
            editor.WriteMessage($"\n  Drawing: {System.IO.Path.GetFileName(doc.Name)}");

            // Part statistics
            var stats = PartPropertiesManager.GetPartStatistics();
            editor.WriteMessage($"\n  Total 3D Solids: {stats.totalSolids}");
            editor.WriteMessage($"\n  Parts with Metadata: {stats.withMetadata}");
            editor.WriteMessage($"\n  Parts without Metadata: {stats.withoutMetadata}");

            editor.WriteMessage("\n============================================");
        }

        /// <summary>
        /// The MC_LIST_PARTS command.
        /// Lists all parts with metadata in the current drawing.
        /// </summary>
        [CommandMethod("MC_LIST_PARTS")]
        public void ListParts()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            var parts = PartPropertiesManager.GetAllPartsInDrawing();

            if (parts.Count == 0)
            {
                editor.WriteMessage("\nNo parts with metadata found in this drawing.");
                editor.WriteMessage("\nUse MC_ADD_PART_METADATA to add metadata to 3D solids.");
                return;
            }

            editor.WriteMessage($"\n========== Parts in Drawing ({parts.Count}) ==========");

            int index = 1;
            foreach (var part in parts)
            {
                editor.WriteMessage($"\n  {index}. {part.PartName} (Handle: {part.ObjectHandle})");
                index++;
            }

            editor.WriteMessage("\n================================================");
        }
    }
}
