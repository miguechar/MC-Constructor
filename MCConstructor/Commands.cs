using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.IO;

namespace MCConstructor
{
    public class Commands
    {
        public const string AppName = "MC_CONSTRUCTOR";

        [CommandMethod("MCGreet")]
        public void Greet()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc.Editor.WriteMessage("\nHello World!");
        }

        [CommandMethod("MCAddPartMetadata")]
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

            var materials = TryGetMaterials(editor);
            var dialog = new PartMetadataDialog(materials);
            if (Application.ShowModalWindow(dialog) != true || string.IsNullOrEmpty(dialog.PartName))
            { editor.WriteMessage("\nCommand cancelled."); return; }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(result.ObjectId, OpenMode.ForWrite) as Entity;
                if (entity == null) { editor.WriteMessage("\nError accessing object."); return; }

                RegisterApp(db, tr);
                entity.XData = BuildPartXData(dialog.PartName, dialog.SelectedMaterial);
                tr.Commit();

                string matInfo = dialog.SelectedMaterial != null ? $", material: {dialog.SelectedMaterial.Name}" : "";
                editor.WriteMessage($"\nMetadata added: {dialog.PartName}{matInfo}");
            }
        }

        private static List<Material> TryGetMaterials(Editor editor)
        {
            try { return MaterialLibraryService.GetMaterials(); }
            catch { return new List<Material>(); }
        }

        private static List<string> TryGetProfileNames(Editor editor)
        {
            try
            {
                var drawings = MaterialLibraryService.GetProfileDrawings();
                var names = new List<string>();
                foreach (var d in drawings) names.Add(d.Name);
                return names;
            }
            catch { return new List<string>(); }
        }

        /// <summary>
        /// MCEditPartMetadata — Edit all metadata on a selected 3D solid.
        /// Pre-fills the dialog with the part's current XData (Part Name,
        /// Material, Profile) and writes back any changes on Apply.
        /// </summary>
        [CommandMethod("MCEditPartMetadata")]
        public void EditPartMetadata()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db  = doc.Database;
            var editor = doc.Editor;

            var opts = new PromptEntityOptions("\nSelect a 3D solid to edit: ");
            opts.SetRejectMessage("\nMust be a 3D solid.");
            opts.AddAllowedClass(typeof(Solid3d), true);

            var result = editor.GetEntity(opts);
            if (result.Status != PromptStatus.OK) { editor.WriteMessage("\nCommand cancelled."); return; }

            // Read current XData
            string curPartName    = "";
            string curMaterialId  = "";
            string curMaterialName = "";
            string curProfileName  = "";
            string handle = "";
            string layer  = "";
            double width = 0, depth = 0, height = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(result.ObjectId, OpenMode.ForRead) as Entity;
                if (entity == null) { editor.WriteMessage("\nCould not read object."); return; }

                handle = entity.Handle.ToString();
                layer  = entity.Layer;

                try
                {
                    var ext = entity.GeometricExtents;
                    width  = ext.MaxPoint.X - ext.MinPoint.X;
                    depth  = ext.MaxPoint.Y - ext.MinPoint.Y;
                    height = ext.MaxPoint.Z - ext.MinPoint.Z;
                }
                catch { }

                var xdata = entity.GetXDataForApplication(AppName);
                if (xdata != null)
                {
                    var vals = xdata.AsArray();
                    for (int i = 1; i + 1 < vals.Length; i += 2)
                    {
                        string key = vals[i].Value.ToString();
                        string val = vals[i + 1].Value.ToString();
                        if      (key == "PartName")     curPartName     = val;
                        else if (key == "MaterialId")   curMaterialId   = val;
                        else if (key == "MaterialName") curMaterialName = val;
                        else if (key == "ProfileName")  curProfileName  = val;
                    }
                }
                tr.Commit();
            }

            var materials    = TryGetMaterials(editor);
            var profileNames = TryGetProfileNames(editor);

            var dialog = new EditPartMetadataDialog(
                curPartName, curMaterialId, curMaterialName, curProfileName,
                handle, layer, width, depth, height,
                materials, profileNames);

            if (Application.ShowModalWindow(dialog) != true)
            { editor.WriteMessage("\nCommand cancelled."); return; }

            // Build the update map and apply via MergeXData
            var updates = new System.Collections.Generic.Dictionary<string, string>
            {
                ["PartName"]     = dialog.NewPartName,
                ["MaterialId"]   = dialog.SelectedMaterial?.Id.ToString()   ?? "",
                ["MaterialName"] = dialog.SelectedMaterial?.Name            ?? "",
                ["ProfileName"]  = dialog.SelectedProfileName               ?? "",
            };

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(result.ObjectId, OpenMode.ForWrite) as Entity;
                if (entity == null) { editor.WriteMessage("\nCould not open object for write."); return; }

                PartGeometryHelper.MergeXData(entity, updates, tr, db);
                tr.Commit();
            }

            editor.WriteMessage($"\nMetadata updated: '{dialog.NewPartName}'");
            if (dialog.SelectedMaterial != null)
                editor.WriteMessage($", material: {dialog.SelectedMaterial.Name}");
            if (!string.IsNullOrEmpty(dialog.SelectedProfileName))
                editor.WriteMessage($", profile: {dialog.SelectedProfileName}");
        }

        /// <summary>
        /// MCAssignMaterial — Assign a material grade to multiple selected
        /// entities in one operation. Preserves all other XData keys.
        /// </summary>
        [CommandMethod("MCAssignMaterial")]
        public void AssignMaterial()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db  = doc.Database;
            var editor = doc.Editor;

            var selOpts = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect objects to assign material to: ",
                AllowDuplicates  = false,
            };

            var selResult = editor.GetSelection(selOpts);
            if (selResult.Status == PromptStatus.Cancel)
            { editor.WriteMessage("\nCommand cancelled."); return; }
            if (selResult.Status != PromptStatus.OK || selResult.Value.Count == 0)
            { editor.WriteMessage("\nNo objects selected."); return; }

            var materials = TryGetMaterials(editor);
            if (materials.Count == 0)
            {
                editor.WriteMessage("\nNo materials in the project library. Add materials via MCMaterialLibrary first.");
                return;
            }

            var dialog = new AssignMaterialDialog(selResult.Value.Count, materials);
            if (Application.ShowModalWindow(dialog) != true)
            { editor.WriteMessage("\nCommand cancelled."); return; }

            var mat = dialog.SelectedMaterial;
            var updates = new System.Collections.Generic.Dictionary<string, string>
            {
                ["MaterialId"]   = mat.Id.ToString(),
                ["MaterialName"] = mat.Name,
            };

            int updated = 0, skipped = 0;
            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                RegisterApp(db, tr);
                foreach (SelectedObject sel in selResult.Value)
                {
                    try
                    {
                        var entity = tr.GetObject(sel.ObjectId, OpenMode.ForWrite) as Entity;
                        if (entity == null) { skipped++; continue; }
                        PartGeometryHelper.MergeXData(entity, updates, tr, db);
                        updated++;
                    }
                    catch { skipped++; }
                }
                tr.Commit();
            }

            editor.WriteMessage($"\nMaterial '{mat.Name}' assigned to {updated} object(s).");
            if (skipped > 0)
                editor.WriteMessage($" ({skipped} skipped)");
        }

        private static ResultBuffer BuildPartXData(string partName, Material material)
        {
            var items = new System.Collections.Generic.List<TypedValue>
            {
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "PartName"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, partName ?? ""),
            };
            if (material != null)
            {
                items.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, "MaterialId"));
                items.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, material.Id.ToString()));
                items.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, "MaterialName"));
                items.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, material.Name));
            }
            return new ResultBuffer(items.ToArray());
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

        /// <summary>
        /// MCBatchAddPartNames - Apply part names to a multi-object selection
        /// in one shot. Pops a dialog asking for a prefix and a starting
        /// serial; each selected entity then gets XData where PartName =
        /// "{prefix}-{serial}", with the serial auto-incrementing per object.
        ///
        /// Example: select 2 entities, prefix "A1LB1-00-D07", start 100 ->
        /// the two entities are named "A1LB1-00-D07-100" and
        /// "A1LB1-00-D07-101".
        ///
        /// Accepts any entity type (3D solid, polyline, region, ...) so the
        /// user can label 2D outlines as well as fabrication parts.
        /// </summary>
        [CommandMethod("MCBatchAddPartNames")]
        public void BatchAddPartNames()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            // Object selection. We accept any entity - XData attaches to
            // anything derived from Entity, and the user might want to name
            // 2D outlines (polylines, circles) as well as 3D solids.
            var selOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect objects to name (any entity): ",
                AllowDuplicates = false
            };

            var selResult = editor.GetSelection(selOptions);

            if (selResult.Status == PromptStatus.Cancel)
            {
                editor.WriteMessage("\nCommand cancelled.");
                return;
            }
            if (selResult.Status != PromptStatus.OK || selResult.Value.Count == 0)
            {
                editor.WriteMessage("\nNo objects selected.");
                return;
            }

            int count = selResult.Value.Count;

            var materials = TryGetMaterials(editor);
            var dialog = new BatchPartNameDialog(count, materials);
            if (Application.ShowModalWindow(dialog) != true)
            {
                editor.WriteMessage("\nCommand cancelled.");
                return;
            }

            string prefix = dialog.Prefix ?? "";
            int serial = dialog.StartingSerial;
            var batchMaterial = dialog.SelectedMaterial;

            // Apply names in a single transaction so a mid-sequence error
            // either commits the whole batch or rolls it back, never half.
            int updated = 0;
            int skipped = 0;
            string firstApplied = null;
            string lastApplied = null;

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                RegisterApp(db, tr);

                foreach (SelectedObject sel in selResult.Value)
                {
                    var entity = tr.GetObject(sel.ObjectId, OpenMode.ForWrite) as Entity;
                    if (entity == null) { skipped++; continue; }

                    string partName = BatchPartNameDialog.BuildName(prefix, serial);

                    try
                    {
                        entity.XData = BuildPartXData(partName, batchMaterial);
                        if (firstApplied == null) firstApplied = partName;
                        lastApplied = partName;
                        updated++;
                        serial++;
                    }
                    catch (System.Exception ex)
                    {
                        editor.WriteMessage($"\n  Skipped {entity.GetType().Name}: {ex.Message}");
                        skipped++;
                    }
                }

                tr.Commit();
            }

            editor.WriteMessage($"\n========== Batch Naming Complete ==========");
            editor.WriteMessage($"\n  Names applied: {updated}");
            if (firstApplied != null)
            {
                if (updated == 1)
                    editor.WriteMessage($"\n  Name:          {firstApplied}");
                else
                    editor.WriteMessage($"\n  Range:         {firstApplied}  ...  {lastApplied}");
            }
            if (skipped > 0)
                editor.WriteMessage($"\n  Skipped:       {skipped} (could not write XData)");
            editor.WriteMessage($"\n===========================================");

            // Refresh the metadata palette so newly named parts show up
            // immediately if the palette is open.
            try { MetadataPalette.RefreshProjectInfo(); } catch { }
        }

        [CommandMethod("MCViewPartMetadata")]
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

        [CommandMethod("MCShowMetadataPalette")]
        public void ShowMetadataPalette() => MetadataPalette.Toggle();

        /// <summary>
        /// MCMaterialLibrary - Open the per-project material library editor
        /// where the user manages steel grades, stock plate sizes, and
        /// extrudable profiles. Requires an open project; the library is
        /// scoped per-project so different jobs can carry different specs.
        /// </summary>
        [CommandMethod("MCMaterialLibrary")]
        public void OpenMaterialLibrary()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc?.Editor;

            if (DatabaseService.CurrentProject == null)
            {
                editor?.WriteMessage("\nNo project open. Use MCOpenProject first.");
                return;
            }

            try
            {
                var window = new MaterialLibraryWindow();
                Application.ShowModalWindow(window);
            }
            catch (System.Exception ex)
            {
                editor?.WriteMessage($"\nMaterial Library error: {ex.Message}");
            }
        }

        [CommandMethod("MCOpenProject")]
        public void OpenProject()
        {
            var editor = Application.DocumentManager.MdiActiveDocument.Editor;
            var dialog = new ProjectDialog();

            if (Application.ShowModalWindow(dialog) == true && dialog.SelectedProject != null)
            {
                DatabaseService.SetCurrentProject(dialog.SelectedProject);
                editor.WriteMessage($"\nProject opened: {dialog.SelectedProject.Name}");
                MetadataPalette.RefreshProjectInfo();

                // Auto-create pro.json for existing postgres projects that don't have one yet.
                if (!string.IsNullOrEmpty(dialog.SelectedProject.Directory)
                    && System.IO.Directory.Exists(dialog.SelectedProject.Directory))
                {
                    string jsonPath = System.IO.Path.Combine(dialog.SelectedProject.Directory, "pro.json");
                    if (!System.IO.File.Exists(jsonPath))
                    {
                        try
                        {
                            ProjectFileWriter.WriteNew(jsonPath, dialog.SelectedProject,
                                StorageRouter.ActiveMode ?? "postgres",
                                DatabaseService.GetConnectionString());
                            editor.WriteMessage($"\nCreated pro.json for offline access: {jsonPath}");
                        }
                        catch { /* non-fatal */ }
                    }
                }
            }
        }

        /// <summary>
        /// MCCreateProject - Create a new project: insert a row in
        /// public.projects, then build the standard folder structure on disk
        /// under the chosen parent directory. The new project is set as the
        /// current project on success.
        /// </summary>
        [CommandMethod("MCCreateProject")]
        public void CreateProject()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            // If postgres is configured but unreachable, fall back to JSON rather
            // than blocking project creation entirely.
            bool forceJson = false;
            if (StorageRouter.IsInitialized && StorageRouter.ActiveMode == "postgres"
                && !DatabaseService.TestConnection(out var connError))
            {
                editor.WriteMessage($"\nDatabase not reachable ({connError}).");
                editor.WriteMessage("\nNo database required — creating project in offline (JSON) mode.");
                forceJson = true;
            }

            var defaultParent = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var dialog = new CreateProjectDialog(defaultParent);

            if (Application.ShowModalWindow(dialog) != true)
            {
                editor.WriteMessage("\nCommand cancelled.");
                return;
            }

            // Block duplicate names early (skip when switching to JSON — the JSON
            // provider uses a single-project file so duplicates aren't meaningful).
            if (StorageRouter.IsInitialized && !forceJson)
            {
                try
                {
                    if (DatabaseService.ProjectNameExists(dialog.ProjectName))
                    {
                        editor.WriteMessage($"\nA project named '{dialog.ProjectName}' already exists.");
                        return;
                    }
                }
                catch (System.Exception ex)
                {
                    editor.WriteMessage($"\nError checking for existing project: {ex.Message}");
                    return;
                }
            }

            // Create the folder layout first so a database row never points
            // at a non-existent directory.
            try
            {
                ProjectFolderLayout.CreateLayout(dialog.ProjectRoot);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nError creating folder structure: {ex.Message}");
                return;
            }

            // Use JSON if no provider is active yet, or if postgres was unreachable.
            if (!StorageRouter.IsInitialized || forceJson)
            {
                string bootstrapJson = System.IO.Path.Combine(dialog.ProjectRoot, "pro.json");
                ProjectFileWriter.WriteNew(bootstrapJson, new Project
                {
                    Id = Guid.NewGuid(),
                    Name = dialog.ProjectName,
                    Description = dialog.Description,
                    Directory = dialog.ProjectRoot
                }, "json", null);
                StorageRouter.UseJson(bootstrapJson);
            }

            // Insert the project row.
            Project project;
            try
            {
                project = DatabaseService.CreateProject(
                    dialog.ProjectName,
                    dialog.Description,
                    dialog.ProjectRoot);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nError saving project: {ex.Message}");
                editor.WriteMessage("\nFolders were created but the project record was not.");
                return;
            }

            DatabaseService.SetCurrentProject(project);
            MetadataPalette.RefreshProjectInfo();

            // Write / update pro.json in the project root.
            try
            {
                string jsonPath = System.IO.Path.Combine(project.Directory, "pro.json");
                ProjectFileWriter.WriteNew(jsonPath, project,
                    StorageRouter.ActiveMode ?? "json",
                    DatabaseService.GetConnectionString());
                editor.WriteMessage($"\n  JSON:      {jsonPath}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nNote: Could not write pro.json: {ex.Message}");
            }

            editor.WriteMessage("\n========== Project Created ==========");
            editor.WriteMessage($"\n  Name:      {project.Name}");
            editor.WriteMessage($"\n  Directory: {project.Directory}");
            editor.WriteMessage($"\n  Mode:      {StorageRouter.ActiveMode}");
            editor.WriteMessage($"\n  Folders:   {ProjectFolderLayout.AllFolders.Count} created");
            editor.WriteMessage("\n=====================================");
        }

        /// <summary>
        /// MCCreateDrawing - Create a new .dwg file in the current project,
        /// routed into the proper subfolder by type/discipline. The drawing
        /// is registered in public.drawings, the type/discipline are stored
        /// as drawing properties in the file's SummaryInfo, and the document
        /// is opened in AutoCAD.
        /// </summary>
        [CommandMethod("MCCreateDrawing", CommandFlags.Session)]
        public void CreateDrawing()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            var project = DatabaseService.CurrentProject;
            if (project == null)
            {
                editor.WriteMessage("\nNo project open. Use MCOpenProject or MCCreateProject first.");
                return;
            }
            if (string.IsNullOrEmpty(project.Directory))
            {
                editor.WriteMessage("\nCurrent project has no directory recorded. Re-create or update it.");
                return;
            }
            if (!Directory.Exists(project.Directory))
            {
                editor.WriteMessage($"\nProject directory does not exist on disk:\n  {project.Directory}");
                return;
            }

            var dialog = new CreateDrawingDialog(project);
            if (Application.ShowModalWindow(dialog) != true)
            {
                editor.WriteMessage("\nCommand cancelled.");
                return;
            }

            // Duplicate name check (per project) before doing any I/O.
            try
            {
                if (DatabaseService.DrawingNameExistsInCurrentProject(dialog.DrawingName))
                {
                    editor.WriteMessage($"\nA drawing named '{dialog.DrawingName}' already exists in this project.");
                    return;
                }
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nError checking for existing drawing: {ex.Message}");
                return;
            }

            // Make sure the destination folder exists (in case the project
            // was created before this layout existed).
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dialog.TargetFilePath));
            }
            catch (System.Exception ex)
            {
                WriteMessageSafe($"\nError creating target folder: {ex.Message}");
                return;
            }

            // Pre-allocate the drawing id so we can stamp it into the file
            // BEFORE SaveAs. This avoids the "open then SaveAs the active
            // document to its own path" pattern, which throws eNotApplicable.
            var drawingId = Guid.NewGuid();

            // When a base is chosen it is overlaid as a live XRef, not copied.
            // The drawing itself starts from the template (or blank).
            try
            {
                WriteDrawingFile(
                    dialog.TargetFilePath,
                    dialog.TemplatePath,
                    new DrawingPropertiesData
                    {
                        DrawingType     = dialog.DrawingType,
                        Discipline      = dialog.Discipline,
                        ProjectId       = project.Id,
                        ProjectName     = project.Name,
                        DrawingId       = drawingId,
                        Description     = dialog.Description,
                        BaseDrawingPath = dialog.BasePath   // null when no base chosen
                    });
            }
            catch (System.Exception ex)
            {
                WriteMessageSafe($"\nError creating drawing file: {ex.Message}");
                // Best-effort cleanup so a partial file doesn't block a retry.
                try { if (File.Exists(dialog.TargetFilePath)) File.Delete(dialog.TargetFilePath); }
                catch { }
                return;
            }

            // Register in DB using the pre-allocated id.
            Drawing drawingRow;
            try
            {
                drawingRow = DatabaseService.CreateDrawing(
                    drawingId,
                    dialog.DrawingName,
                    dialog.DrawingType,
                    dialog.Discipline,
                    dialog.TargetFilePath,
                    dialog.Description);
            }
            catch (System.Exception ex)
            {
                WriteMessageSafe($"\nDrawing file was created but DB insert failed: {ex.Message}");
                WriteMessageSafe($"\nFile location: {dialog.TargetFilePath}");
                return;
            }

            // Open the freshly-saved drawing. Note: this may switch the active
            // document, so use WriteMessageSafe (which always re-fetches the
            // current editor) for any subsequent messages.
            try
            {
                Application.DocumentManager.Open(dialog.TargetFilePath, false);
            }
            catch (System.Exception ex)
            {
                WriteMessageSafe($"\nDrawing created but could not be opened: {ex.Message}");
            }

            WriteMessageSafe("\n========== Drawing Created ==========");
            WriteMessageSafe($"\n  Name:       {drawingRow.Name}");
            WriteMessageSafe($"\n  Type:       {DrawingTypes.DisplayName(drawingRow.DrawingType)}");
            if (!string.IsNullOrEmpty(drawingRow.Discipline))
                WriteMessageSafe($"\n  Discipline: {drawingRow.Discipline}");
            if (!string.IsNullOrEmpty(dialog.BasePath))
                WriteMessageSafe($"\n  Base XRef:  {Path.GetFileNameWithoutExtension(dialog.BasePath)}");
            else if (!string.IsNullOrEmpty(dialog.TemplatePath))
                WriteMessageSafe($"\n  Template:   {Path.GetFileName(dialog.TemplatePath)}");
            WriteMessageSafe($"\n  Path:       {drawingRow.FilePath}");
            WriteMessageSafe("\n======================================");
        }

        /// <summary>
        /// Write the new drawing to <paramref name="targetPath"/> and stamp
        /// the MC drawing properties into it.
        ///
        /// If <paramref name="templatePath"/> is provided, the file is
        /// produced by copying the template (File.Copy) rather than
        /// ReadDwgFile-ing it. ReadDwgFile uses stricter locking and can
        /// trip eFileSharingViolation on a .dwg template that no one is
        /// actively writing - File.Copy handles that case fine. Once the
        /// copy is on disk we re-open it as a side-database to stamp the
        /// MC properties and save it back.
        ///
        /// When no template is given, a default empty database is saved out
        /// first and then re-opened the same way.
        /// </summary>
        private static void WriteDrawingFile(string targetPath, string templatePath, DrawingPropertiesData properties)
        {
            // Step 1: produce the .dwg on disk.
            if (!string.IsNullOrEmpty(templatePath) && File.Exists(templatePath))
            {
                // overwrite:false because the dialog already validated that
                // the target file does not exist.
                File.Copy(templatePath, targetPath, overwrite: false);
            }
            else
            {
                using (var emptyDb = new Database(true, true))
                {
                    emptyDb.SaveAs(targetPath, DwgVersion.Current);
                }
            }

            // Step 2: stamp MC properties and attach base drawing as an overlay
            // XRef if one was chosen. Re-open as a side-database (noDocument=true)
            // so we can write without disturbing the active document.
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(targetPath, FileShare.ReadWrite, true, "");
                db.CloseInput(true);
                DrawingPropertiesManager.Write(db, properties);

                if (!string.IsNullOrEmpty(properties.BaseDrawingPath) && File.Exists(properties.BaseDrawingPath))
                    OverlayBaseXref(db, properties.BaseDrawingPath);

                db.SaveAs(targetPath, DwgVersion.Current);
            }
        }

        /// <summary>
        /// Overlays <paramref name="basePath"/> as an XRef (overlay mode) in
        /// <paramref name="db"/> and inserts a block reference at the origin.
        /// Overlay mode means the base is not inherited by drawings that later
        /// xref this drawing, preventing unintended reference chains.
        /// </summary>
        private static void OverlayBaseXref(Database db, string basePath)
        {
            string blockName = XrefBlockName(basePath);
            try
            {
                ObjectId xrefBtrId = db.OverlayXref(basePath, blockName);
                if (xrefBtrId.IsNull) return;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    using (var br = new BlockReference(Point3d.Origin, xrefBtrId))
                    {
                        ms.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                    }
                    tr.Commit();
                }
            }
            catch
            {
                // Non-fatal: drawing is still created, just without the XRef.
                // AutoCAD may reject the overlay if the block name conflicts.
            }
        }

        /// <summary>
        /// Derives a valid AutoCAD block name from a file path (filename
        /// without extension, with non-alphanumeric characters replaced by '_').
        /// </summary>
        private static string XrefBlockName(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath);
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            string result = sb.ToString();
            if (string.IsNullOrEmpty(result)) return "BASE";
            // Block names cannot start with a digit.
            return char.IsDigit(result[0]) ? "_" + result : result;
        }

        /// <summary>
        /// Finds the absolute path of an XRef stored in a BlockTableRecord,
        /// resolving relative paths against the host drawing's directory.
        /// </summary>
        private static string ResolveXrefPath(string storedPath, string hostDrawingPath)
        {
            if (string.IsNullOrEmpty(storedPath)) return null;
            if (Path.IsPathRooted(storedPath)) return storedPath;
            try
            {
                string dir = Path.GetDirectoryName(hostDrawingPath) ?? "";
                return Path.GetFullPath(Path.Combine(dir, storedPath));
            }
            catch { return storedPath; }
        }

        /// <summary>
        /// MCUpdateBase — scans every drawing in the current project for an
        /// overlay XRef that points to a chosen base drawing, reports the
        /// affected drawings, and immediately reloads the XRef in any
        /// documents that are already open in this AutoCAD session.
        /// Closed drawings pick up the changes automatically the next time
        /// they are opened, because XRefs are never baked into the host file.
        /// </summary>
        [CommandMethod("MCUpdateBase", CommandFlags.Session)]
        public void UpdateBase()
        {
            var editor = Application.DocumentManager.MdiActiveDocument?.Editor;

            var project = DatabaseService.CurrentProject;
            if (project == null)
            {
                editor?.WriteMessage("\nNo project open. Use MCOpenProject first.");
                return;
            }

            List<Drawing> baseDrawings;
            List<Drawing> allDrawings;
            try
            {
                allDrawings  = DatabaseService.GetDrawings();
                baseDrawings = allDrawings.FindAll(d =>
                    d.DrawingType == DrawingTypes.Base && File.Exists(d.FilePath));
            }
            catch (System.Exception ex)
            {
                editor?.WriteMessage($"\nError loading drawings: {ex.Message}");
                return;
            }

            if (baseDrawings.Count == 0)
            {
                editor?.WriteMessage("\nNo base drawings found in this project.");
                editor?.WriteMessage("\nCreate one with MCCreateDrawing using drawing type \"Base Drawing\".");
                return;
            }

            var dialog = new UpdateBaseDialog(baseDrawings, allDrawings);
            Application.ShowModalWindow(dialog);
        }

        /// <summary>
        /// Write a message to whichever document is active right now. Safe to
        /// call after operations that switch the active document (creating
        /// or opening a new drawing) - never throws.
        /// </summary>
        private static void WriteMessageSafe(string message)
        {
            try
            {
                var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage(message);
            }
            catch { /* nothing useful we can do here */ }
        }

        /// <summary>
        /// MCNavigator - Open the project navigator window. Lists every
        /// drawing registered for the current project, grouped by Function /
        /// Detail (and Standards / Sheets), shows a thumbnail preview, and
        /// lets the user open one with a single click.
        /// </summary>
        [CommandMethod("MCNavigator", CommandFlags.Session)]
        public void OpenNavigator()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc?.Editor;

            var project = DatabaseService.CurrentProject;
            if (project == null)
            {
                editor?.WriteMessage("\nNo project open. Use MCOpenProject first.");
                return;
            }
            if (string.IsNullOrEmpty(project.Directory))
            {
                editor?.WriteMessage("\nCurrent project has no directory recorded. Re-create or update it.");
                return;
            }

            string toOpen = null;
            try
            {
                var window = new NavigatorWindow(project);
                Application.ShowModalWindow(window);
                toOpen = window.RequestedOpenPath;
            }
            catch (System.Exception ex)
            {
                WriteMessageSafe($"\nNavigator error: {ex.Message}");
                return;
            }

            // Defer the actual document open until after the modal window has
            // closed. Calling DocumentManager.Open from inside the navigator
            // throws "Invalid execution context" because the command engine
            // will not run that operation while a modal dialog is up.
            if (!string.IsNullOrEmpty(toOpen))
            {
                if (!File.Exists(toOpen))
                {
                    WriteMessageSafe($"\nFile no longer exists on disk: {toOpen}");
                    return;
                }
                try
                {
                    Application.DocumentManager.Open(toOpen, false);
                }
                catch (System.Exception ex)
                {
                    WriteMessageSafe($"\nCould not open drawing: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// MCDrawingProperties - View / edit the MC drawing properties
        /// (type, discipline, description) on the active drawing. Updates
        /// both the file's SummaryInfo and the matching public.drawings row
        /// when the drawing is registered.
        /// </summary>
        [CommandMethod("MCDrawingProperties")]
        public void EditDrawingProperties()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var editor = doc.Editor;

            var current = DrawingPropertiesManager.GetCurrent();

            // If the file doesn't already have ProjectName stamped, fall
            // back to the open project for nicer display in the dialog.
            if (string.IsNullOrEmpty(current.ProjectName) && DatabaseService.CurrentProject != null)
            {
                current.ProjectName = DatabaseService.CurrentProject.Name;
                current.ProjectId   = DatabaseService.CurrentProject.Id;
            }

            var dialog = new DrawingPropertiesDialog(current);
            if (Application.ShowModalWindow(dialog) != true)
            {
                editor.WriteMessage("\nCommand cancelled.");
                return;
            }

            var result = dialog.Result;
            // Preserve the project link / drawing id from before.
            result.ProjectId   = current.ProjectId;
            result.ProjectName = current.ProjectName;
            result.DrawingId   = current.DrawingId;

            try
            {
                DrawingPropertiesManager.SetCurrent(result);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nError writing drawing properties: {ex.Message}");
                return;
            }

            // If this drawing has a matching DB row, update it too. We try
            // by DrawingId first, then by file path as a fallback.
            try
            {
                Drawing row = null;
                if (current.DrawingId.HasValue)
                {
                    // No GetById currently, but GetDrawingByPath covers most
                    // cases since the file lives at doc.Name.
                }
                if (row == null && !string.IsNullOrEmpty(doc.Name))
                {
                    row = DatabaseService.GetDrawingByPath(doc.Name);
                }
                if (row != null)
                {
                    DatabaseService.UpdateDrawingProperties(
                        row.Id, result.DrawingType, result.Discipline, result.Description);
                    editor.WriteMessage("\nDrawing properties saved (file + database).");
                }
                else
                {
                    editor.WriteMessage("\nDrawing properties saved (file only - no matching DB row).");
                }
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nDrawing properties saved to file, but DB update failed: {ex.Message}");
                return;
            }
        }

        [CommandMethod("MCConfigDatabase")]
        public void ConfigDatabase()
        {
            var dialog = new DatabaseConfigDialog();
            if (Application.ShowModalWindow(dialog) == true)
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nDatabase config saved.");
        }

        [CommandMethod("MCSaveParts")]
        public void SaveParts()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            if (DatabaseService.CurrentProject == null)
            { editor.WriteMessage("\nNo project open. Use MCOpenProject first."); return; }

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

        [CommandMethod("MCProjectStatus")]
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

        [CommandMethod("MCListParts")]
        public void ListParts()
        {
            var editor = Application.DocumentManager.MdiActiveDocument.Editor;
            var parts = PartPropertiesManager.GetAllPartsInDrawing();

            if (parts.Count == 0)
            { editor.WriteMessage("\nNo parts found. Use MCAddPartMetadata first."); return; }

            editor.WriteMessage($"\n========== Parts ({parts.Count}) ==========");
            for (int i = 0; i < parts.Count; i++)
                editor.WriteMessage($"\n  {i + 1}. {parts[i].PartName}");
            editor.WriteMessage("\n==========================================");
        }

        // ==================== NEW PART REFERENCE COMMANDS ====================

        /// <summary>
        /// MCInsertPart - Insert a part from the database into the current drawing.
        /// Works like xref but the part is editable.
        /// </summary>
        [CommandMethod("MCInsertPart")]
        public void InsertPart()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            if (DatabaseService.CurrentProject == null)
            { editor.WriteMessage("\nNo project open. Use MCOpenProject first."); return; }

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
                    editor.WriteMessage("\nThis is a reference. Use MCOverridePart to make local changes.");
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
        /// MCOverridePart - Override a part reference with local changes.
        /// Changes will sync back to original when MCUpdateAllParts is run.
        /// </summary>
        [CommandMethod("MCOverridePart")]
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
                    editor.WriteMessage("\nRun MCUpdateAllParts in the original drawing to sync changes.");
                }
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nError: {ex.Message}");
            }
        }

        /// <summary>
        /// MCUpdateAllParts - Update all parts in the drawing from database.
        /// Applies changes from overridden references.
        /// </summary>
        [CommandMethod("MCUpdateAllParts")]
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
        /// MCListDbParts - List all parts in the database for current project.
        /// </summary>
        [CommandMethod("MCListDbParts")]
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

        // ==================== NESTING COMMANDS ====================

        /// <summary>
        /// MCCreateNest - Create a nesting layout for plasma cutting.
        /// Prompts to select parts, then asks for plate dimensions,
        /// creates a new drawing with flattened 2D outlines optimized on the plate.
        /// </summary>
        [CommandMethod("MCCreateNest", CommandFlags.Session)]
        public void CreateNest()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            editor.WriteMessage("\n=== MC Create Nesting Layout ===");

            // Prompt user to select parts to nest. We accept both 3D solids
            // and 2D shapes - parts without MC metadata get arranged on the
            // plate without labels, so users can nest raw outlines directly.
            var selOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect parts to nest (3D solids or 2D shapes): ",
                AllowDuplicates = false
            };

            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator,  "<OR"),
                new TypedValue((int)DxfCode.Start,     "3DSOLID"),
                new TypedValue((int)DxfCode.Start,     "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start,     "POLYLINE"),
                new TypedValue((int)DxfCode.Start,     "REGION"),
                new TypedValue((int)DxfCode.Start,     "CIRCLE"),
                new TypedValue((int)DxfCode.Start,     "ELLIPSE"),
                new TypedValue((int)DxfCode.Start,     "SPLINE"),
                new TypedValue((int)DxfCode.Start,     "SOLID"),
                new TypedValue((int)DxfCode.Operator,  "OR>"),
            });

            var selResult = editor.GetSelection(selOptions, filter);

            if (selResult.Status == PromptStatus.Cancel)
            {
                editor.WriteMessage("\nCommand cancelled.");
                return;
            }

            if (selResult.Status != PromptStatus.OK || selResult.Value.Count == 0)
            {
                editor.WriteMessage("\nNo 3D solids selected.");
                return;
            }

            editor.WriteMessage($"\nSelected {selResult.Value.Count} solid(s).");

            // Extract part information
            var parts = NestingService.ExtractPartsFromSelection(selResult.Value);

            if (parts.Count == 0)
            {
                editor.WriteMessage("\nNo valid parts found in selection.");
                return;
            }

            // Show plate dimensions dialog
            var dialog = new NestingDialog(parts.Count);
            if (Application.ShowModalWindow(dialog) != true)
            {
                editor.WriteMessage("\nCommand cancelled.");
                return;
            }

            if (dialog.SelectedPlate != null)
                editor.WriteMessage($"\nPlate: {dialog.SelectedPlate.Code} ({dialog.SelectedPlate.MaterialName})" +
                    $"  {dialog.PlateWidth} × {dialog.PlateHeight} mm  t={dialog.PlateThickness} mm");
            else
                editor.WriteMessage($"\nPlate size: {dialog.PlateWidth} x {dialog.PlateHeight} mm");
            editor.WriteMessage($"\nPart spacing: {dialog.Spacing} mm");

            // Perform nesting
            editor.WriteMessage("\nCalculating optimal layout...");

            var nestingResult = NestingService.NestPartsGuillotine(
                parts,
                dialog.PlateWidth,
                dialog.PlateHeight,
                dialog.Spacing
            );

            if (nestingResult.PlacedParts.Count == 0)
            {
                editor.WriteMessage("\nError: No parts could fit on the plate. Try a larger plate size.");
                return;
            }

            // Create the nesting drawing
            editor.WriteMessage("\nCreating nesting drawing...");

            try
            {
                // Resolve the project's NEST TEMPLATE if there is one. When
                // present, the new nest is built as a copy of the template
                // (so the title block comes along for free) and registered
                // as a Sheet drawing in the project.
                var nestPlan = ResolveNestOutputPlan(editor, nestingResult);

                NestingService.CreateNestingDrawing(
                    nestingResult,
                    nestPlan.TemplatePath,
                    nestPlan.OutputPath,
                    nestPlan.PropertiesToStamp);

                // If we wrote a real file, register it in the database so
                // the navigator can find it.
                RegisterNestDrawingIfSaved(nestPlan, nestingResult);
                RegisterNestRecord(nestPlan, nestingResult, dialog.SelectedPlate, dialog.PlateWidth, dialog.PlateHeight);

                // CreateNestingDrawing makes a new doc the active document, so
                // the editor we captured above is no longer applicable. Use
                // WriteMessageSafe (which always re-fetches the active editor).
                WriteMessageSafe("\n=== Nesting Complete ===");
                WriteMessageSafe($"\n  Parts placed: {nestingResult.PlacedParts.Count}");

                if (nestingResult.UnplacedParts.Count > 0)
                {
                    WriteMessageSafe($"\n  Parts that didn't fit: {nestingResult.UnplacedParts.Count}");
                    foreach (var unplaced in nestingResult.UnplacedParts)
                    {
                        WriteMessageSafe($"\n    - {unplaced.PartName} ({unplaced.Width:F1} x {unplaced.Height:F1} mm)");
                    }
                }

                WriteMessageSafe($"\n  Plate utilization: {nestingResult.Efficiency:F1}%");
                if (!string.IsNullOrEmpty(nestPlan.OutputPath))
                    WriteMessageSafe($"\n  Saved to: {nestPlan.OutputPath}");
                WriteMessageSafe("\n========================");
            }
            catch (System.Exception ex)
            {
                WriteMessageSafe($"\nError creating nesting drawing: {ex.Message}");
            }
        }

        /// <summary>
        /// Plan describing where a nest drawing should be written and what
        /// MC properties to stamp into it. Built once per nest run so
        /// MCCreateNest and MCQuickNest share the same logic.
        /// </summary>
        private class NestOutputPlan
        {
            public string TemplatePath;       // null when no NEST TEMPLATE found
            public string OutputPath;         // null when no template (in-memory drawing)
            public string DrawingName;        // file stem to use for the DB row
            public Guid   DrawingId;          // pre-allocated id, stamped into the file
            public DrawingPropertiesData PropertiesToStamp;
        }

        /// <summary>
        /// Look up the project's NEST TEMPLATE and decide where to save the
        /// new nest. Returns a plan with all paths null when there's no
        /// project, no template, or anything else that should fall back to
        /// the legacy in-memory flow. Writes a hint to the editor either way.
        /// </summary>
        private static NestOutputPlan ResolveNestOutputPlan(Editor editor, NestingResult nestingResult)
        {
            var plan = new NestOutputPlan();

            var project = DatabaseService.CurrentProject;
            if (project == null || string.IsNullOrEmpty(project.Directory))
            {
                editor.WriteMessage("\nNo project open - creating a blank nest drawing.");
                editor.WriteMessage("\nTip: open a project (MCOpenProject) and add a Template named 'NEST TEMPLATE' to get a titleblock.");
                return plan;
            }

            Drawing template = null;
            try { template = DatabaseService.FindNestTemplate(); }
            catch (System.Exception ex) { editor.WriteMessage($"\nError looking up nest template: {ex.Message}"); }

            if (template == null || string.IsNullOrEmpty(template.FilePath) || !File.Exists(template.FilePath))
            {
                editor.WriteMessage("\nNo NEST TEMPLATE found in this project - creating a blank nest drawing.");
                editor.WriteMessage("\nTip: create a Template named 'NEST TEMPLATE' via MCCreateDrawing and save your titleblock in it.");
                return plan;
            }

            // Build a unique output path under <project>/03 Sheets so the
            // user gets a fresh file every time and the DB unique-name
            // constraint isn't violated.
            string sheetsDir = Path.Combine(project.Directory, ProjectFolderLayout.Sheets);
            string drawingName = $"Nest_{DateTime.Now:yyyy-MM-dd_HHmmss}";
            string outputPath = Path.Combine(sheetsDir, drawingName + ".dwg");

            plan.TemplatePath = template.FilePath;
            plan.OutputPath = outputPath;
            plan.DrawingName = drawingName;
            plan.DrawingId = Guid.NewGuid();
            plan.PropertiesToStamp = new DrawingPropertiesData
            {
                DrawingType = DrawingTypes.Sheet,
                ProjectId   = project.Id,
                ProjectName = project.Name,
                DrawingId   = plan.DrawingId,
                Description = $"Nest layout: {nestingResult.PlacedParts.Count} parts, {nestingResult.Efficiency:F1}% efficiency"
            };

            editor.WriteMessage($"\nUsing nest template: {Path.GetFileName(plan.TemplatePath)}");
            return plan;
        }

        /// <summary>
        /// Register a nest drawing in public.drawings as a Sheet, using the
        /// id we already stamped into the file. Best-effort: if the DB
        /// insert fails, the .dwg still exists on disk.
        /// </summary>
        private static void RegisterNestDrawingIfSaved(NestOutputPlan plan, NestingResult nestingResult)
        {
            if (string.IsNullOrEmpty(plan.OutputPath) || !File.Exists(plan.OutputPath))
                return;

            try
            {
                DatabaseService.CreateDrawing(
                    plan.DrawingId,
                    plan.DrawingName,
                    DrawingTypes.Sheet,
                    discipline: null,
                    filePath: plan.OutputPath,
                    description: plan.PropertiesToStamp?.Description);
            }
            catch (System.Exception ex)
            {
                WriteMessageSafe($"\nNote: nest file created but DB register failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Create a NestRecord row in public.nests so the Nest Manager can track
        /// this run. Best-effort — a DB failure does not abort the nest.
        /// </summary>
        private static void RegisterNestRecord(
            NestOutputPlan plan,
            NestingResult  nestingResult,
            MaterialPlate  plate,
            double         plateWidth,
            double         plateHeight)
        {
            if (DatabaseService.CurrentProject == null) return;
            if (!StorageRouter.IsInitialized) return;

            try
            {
                string materialName    = plate?.MaterialName;
                string plateCode       = plate?.Code;
                string plateDimensions = plate != null
                    ? $"{plate.Width:0.#} × {plate.Height:0.#} × {plate.Thickness:0.#} mm"
                    : $"{plateWidth:0.#} × {plateHeight:0.#} mm";

                var record = new NestRecord
                {
                    ProjectId       = DatabaseService.CurrentProject.Id,
                    DrawingId       = plan.DrawingId != Guid.Empty ? plan.DrawingId : (Guid?)null,
                    Name            = !string.IsNullOrEmpty(plan.DrawingName)
                                        ? plan.DrawingName
                                        : $"Nest_{DateTime.Now:yyyy-MM-dd_HHmmss}",
                    CreatedAt       = DateTime.UtcNow,
                    MaterialName    = materialName,
                    PlateCode       = plateCode,
                    PlateDimensions = plateDimensions,
                    PartCount       = nestingResult.PlacedParts.Count,
                    Efficiency      = nestingResult.Efficiency,
                    DwgPath         = plan.OutputPath
                };

                DatabaseService.CreateNest(record);
            }
            catch (System.Exception ex)
            {
                WriteMessageSafe($"\nNote: nest record DB insert failed: {ex.Message}");
            }
        }

        /// <summary>
        /// MCQuickNest - Quick nesting with default plate size (2440x1220).
        /// </summary>
        [CommandMethod("MCQuickNest", CommandFlags.Session)]
        public void QuickNest()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            // Select every nestable shape - 3D solids and 2D outlines.
            // 2D shapes without metadata still nest, just without labels.
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator,  "<OR"),
                new TypedValue((int)DxfCode.Start,     "3DSOLID"),
                new TypedValue((int)DxfCode.Start,     "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start,     "POLYLINE"),
                new TypedValue((int)DxfCode.Start,     "REGION"),
                new TypedValue((int)DxfCode.Start,     "CIRCLE"),
                new TypedValue((int)DxfCode.Start,     "ELLIPSE"),
                new TypedValue((int)DxfCode.Start,     "SPLINE"),
                new TypedValue((int)DxfCode.Start,     "SOLID"),
                new TypedValue((int)DxfCode.Operator,  "OR>"),
            });

            var selResult = editor.SelectAll(filter);

            if (selResult.Status != PromptStatus.OK || selResult.Value.Count == 0)
            {
                editor.WriteMessage("\nNo nestable shapes found in drawing.");
                return;
            }

            var parts = NestingService.ExtractPartsFromSelection(selResult.Value);

            if (parts.Count == 0)
            {
                editor.WriteMessage("\nNo valid parts found.");
                return;
            }

            // Use default plate size
            var nestingResult = NestingService.NestPartsGuillotine(parts, 2440, 1220, 10);

            if (nestingResult.PlacedParts.Count > 0)
            {
                try
                {
                    var nestPlan = ResolveNestOutputPlan(editor, nestingResult);

                    NestingService.CreateNestingDrawing(
                        nestingResult,
                        nestPlan.TemplatePath,
                        nestPlan.OutputPath,
                        nestPlan.PropertiesToStamp);

                    RegisterNestDrawingIfSaved(nestPlan, nestingResult);
                    RegisterNestRecord(nestPlan, nestingResult, null, 2440, 1220);

                    // The active document just changed; use the safe variant.
                    WriteMessageSafe($"\nQuick nest complete: {nestingResult.PlacedParts.Count} parts, {nestingResult.Efficiency:F1}% efficiency");
                    if (!string.IsNullOrEmpty(nestPlan.OutputPath))
                        WriteMessageSafe($"\n  Saved to: {nestPlan.OutputPath}");
                }
                catch (System.Exception ex)
                {
                    WriteMessageSafe($"\nError creating nesting drawing: {ex.Message}");
                }
            }
            else
            {
                editor.WriteMessage("\nNo parts could fit on default plate (2440x1220mm).");
            }
        }

        /// <summary>
        /// MCInsertProfile - Pick a Profile cross-section drawing, then either:
        ///   • select one or more Lines → each solid follows that line's direction and length, or
        ///   • press Enter → pick an insertion point and use the dialog length along Z.
        /// </summary>
        [CommandMethod("MCInsertProfile")]
        public void InsertProfile()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            if (DatabaseService.CurrentProject == null)
            { editor.WriteMessage("\nNo project open. Use MCOpenProject first."); return; }

            var profileDrawings = MaterialLibraryService.GetProfileDrawings();
            if (profileDrawings.Count == 0)
            { editor.WriteMessage("\nNo profile drawings found. Create a drawing with type 'Profile' first."); return; }

            var dialog = new InsertProfileDialog(profileDrawings);
            if (Application.ShowModalWindow(dialog) != true || dialog.SelectedDrawing == null)
            { editor.WriteMessage("\nCommand cancelled."); return; }

            var profileDwg = dialog.SelectedDrawing;

            if (!File.Exists(profileDwg.FilePath))
            { editor.WriteMessage($"\nProfile file not found: {profileDwg.FilePath}"); return; }

            // ---------------------------------------------------------------
            // Placement: select lines OR press Enter for a manual point.
            // ---------------------------------------------------------------
            var db = doc.Database;

            var curveFilter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "SPLINE"),
                new TypedValue((int)DxfCode.Start, "ARC"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Operator, "OR>"),
            });

            editor.WriteMessage("\nSelect lines/polylines/splines to follow (or press Enter to specify insertion point):");
            var selResult = editor.GetSelection(curveFilter);

            if (selResult.Status == PromptStatus.OK && selResult.Value.Count > 0)
            {
                // Read line geometry in-transaction; collect curve ObjectIds for deferred sweep.
                var lineGeometry = new List<(Point3d Start, Vector3d Dir, double Len)>();
                var curveIds     = new List<ObjectId>();

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in selResult.Value)
                    {
                        var curve = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Curve;
                        if (curve == null) continue;
                        if (curve is Line line)
                        {
                            if (line.Length < 1e-10) continue;
                            lineGeometry.Add((
                                line.StartPoint,
                                (line.EndPoint - line.StartPoint).GetNormal(),
                                line.Length));
                        }
                        else
                        {
                            curveIds.Add(so.ObjectId);
                        }
                    }
                    tr.Commit();
                }

                if (lineGeometry.Count == 0 && curveIds.Count == 0)
                { editor.WriteMessage("\nNo valid curves found in selection."); return; }

                int ok = 0, failed = 0;

                double rollRad = dialog.RollAngle * Math.PI / 180.0;

                foreach (var (start, dir, len) in lineGeometry)
                {
                    try
                    {
                        var ids = CreateExtrudedProfile(profileDwg.FilePath, len, db);
                        if (ids.Count == 0) { failed++; continue; }

                        var xform = BuildAlignmentTransform(Vector3d.ZAxis, dir, start);
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            foreach (var id in ids)
                            {
                                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                                if (ent == null) continue;
                                ent.TransformBy(xform);
                                if (rollRad != 0.0)
                                    ent.TransformBy(Matrix3d.Rotation(rollRad, dir, start));
                                PartGeometryHelper.SetProfileXData(ent, profileDwg.Name, tr, db);
                            }
                            tr.Commit();
                        }
                        ok++;
                    }
                    catch (System.Exception ex)
                    {
                        editor.WriteMessage($"\nFailed for one line: {ex.Message}");
                        failed++;
                    }
                }

                foreach (var curveId in curveIds)
                {
                    try
                    {
                        // Region is cloned into targetDb; sweep runs entirely inside targetDb.
                        var ids = CreateExtrudedProfileAlongPath(profileDwg.FilePath, curveId, db, rollRad);
                        if (ids.Count == 0) { failed++; continue; }

                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            foreach (var id in ids)
                            {
                                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                                if (ent == null) continue;
                                PartGeometryHelper.SetProfileXData(ent, profileDwg.Name, tr, db);
                            }
                            tr.Commit();
                        }
                        ok++;
                    }
                    catch (System.Exception ex)
                    {
                        editor.WriteMessage($"\nFailed for one curve: {ex.Message}");
                        failed++;
                    }
                }

                editor.WriteMessage($"\nInserted {ok} profile(s) of '{profileDwg.Name}'." +
                    (failed > 0 ? $" {failed} failed." : ""));
            }
            else if (selResult.Status == PromptStatus.Error ||
                     selResult.Status == PromptStatus.None)
            {
                // --- Manual single-point mode ----------------------------------
                var ptResult = editor.GetPoint("\nSpecify insertion point: ");
                if (ptResult.Status != PromptStatus.OK)
                { editor.WriteMessage("\nCommand cancelled."); return; }

                try
                {
                    var ids = CreateExtrudedProfile(profileDwg.FilePath, dialog.Length, db);
                    if (ids.Count == 0)
                    {
                        editor.WriteMessage(
                            "\nFailed to create solid — no closed curves or regions found in the profile drawing.");
                        return;
                    }

                    var xform = BuildAlignmentTransform(Vector3d.ZAxis, Vector3d.ZAxis, ptResult.Value);
                    double ptRollRad = dialog.RollAngle * Math.PI / 180.0;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (var id in ids)
                        {
                            var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                            if (ent == null) continue;
                            ent.TransformBy(xform);
                            if (ptRollRad != 0.0)
                                ent.TransformBy(Matrix3d.Rotation(ptRollRad, Vector3d.ZAxis, ptResult.Value));
                            PartGeometryHelper.SetProfileXData(ent, profileDwg.Name, tr, db);
                        }
                        tr.Commit();
                    }

                    editor.WriteMessage(
                        $"\nInserted '{profileDwg.Name}' — {dialog.Length:F2} units along Z.");
                }
                catch (System.Exception ex)
                {
                    editor.WriteMessage($"\nError: {ex.Message}");
                }
            }
            else
            {
                editor.WriteMessage("\nCommand cancelled.");
            }
        }

        /// <summary>
        /// Builds a Matrix3d that first rotates <paramref name="from"/> onto
        /// <paramref name="to"/>, then displaces the origin to <paramref name="translation"/>.
        /// Used to align a Z-extruded solid with an arbitrary line direction.
        /// </summary>
        private static Matrix3d BuildAlignmentTransform(
            Vector3d from, Vector3d to, Point3d translation)
        {
            from = from.GetNormal();
            to   = to.GetNormal();

            Matrix3d rot;
            if (from.IsParallelTo(to))
            {
                // Parallel — either identity or 180° flip.
                rot = from.DotProduct(to) > 0
                    ? Matrix3d.Identity
                    : Matrix3d.Rotation(Math.PI,
                        from.IsParallelTo(Vector3d.XAxis) ? Vector3d.YAxis : Vector3d.XAxis,
                        Point3d.Origin);
            }
            else
            {
                var axis  = from.CrossProduct(to);
                var angle = from.GetAngleTo(to);
                rot = Matrix3d.Rotation(angle, axis, Point3d.Origin);
            }

            return Matrix3d.Displacement(translation - Point3d.Origin) * rot;
        }

        /// <summary>
        /// Opens the profile drawing as a side database, builds a Solid3d by extruding the
        /// cross-section geometry found in model space (Region or closed curves), then
        /// WblockClones the solid into targetDb at the world origin.
        /// Returns the ObjectIds of the entities added to targetDb.
        /// </summary>
        private static List<ObjectId> CreateExtrudedProfile(
            string profileFilePath, double length, Database targetDb)
        {
            var result = new List<ObjectId>();

            using (var sideDb = new Database(false, true))
            {
                sideDb.ReadDwgFile(profileFilePath, FileShare.ReadWrite, true, "");
                sideDb.CloseInput(true);

                // Build the solid inside the side database so WblockClone can copy it.
                var solidIds = new ObjectIdCollection();
                using (var tr = sideDb.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(sideDb.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var curves = new DBObjectCollection();
                    Region existingRegion = null;

                    foreach (ObjectId id in ms)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead);
                        if (ent is Region r)
                        {
                            existingRegion = r;
                            break; // Prefer a pre-built region if one exists.
                        }
                        if (ent is Curve c)
                            curves.Add(c);
                    }

                    Region regionToExtrude = existingRegion;

                    if (regionToExtrude == null && curves.Count > 0)
                    {
                        // CreateFromCurves returns new (non-DB-resident) Region objects.
                        var created = Region.CreateFromCurves(curves);
                        if (created.Count > 0)
                        {
                            // Add the first region to the side DB so ACIS operations work.
                            var primary = (Region)created[0];
                            ms.AppendEntity(primary);
                            tr.AddNewlyCreatedDBObject(primary, true);

                            // Union any additional regions (e.g. multiple closed loops).
                            for (int i = 1; i < created.Count; i++)
                            {
                                var extra = (Region)created[i];
                                try { primary.BooleanOperation(BooleanOperationType.BoolUnite, extra); }
                                catch { }
                                extra.Dispose();
                            }

                            regionToExtrude = primary;
                        }
                    }

                    if (regionToExtrude != null)
                    {
                        var solid = new Solid3d();
                        // Extrude along the Z axis (perpendicular to the XY cross-section plane).
                        solid.Extrude(regionToExtrude, length, 0.0);
                        ms.AppendEntity(solid);
                        tr.AddNewlyCreatedDBObject(solid, true);
                        solidIds.Add(solid.ObjectId);
                    }

                    tr.Commit();
                }

                if (solidIds.Count == 0) return result;

                // Snapshot target model space so we can identify the newly cloned entities.
                ObjectId targetMsId;
                var preExisting = new HashSet<ObjectId>();
                using (var tr = targetDb.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                    targetMsId = bt[BlockTableRecord.ModelSpace];
                    var ms = (BlockTableRecord)tr.GetObject(targetMsId, OpenMode.ForRead);
                    foreach (ObjectId id in ms) preExisting.Add(id);
                    tr.Commit();
                }

                sideDb.WblockCloneObjects(
                    solidIds, targetMsId, new IdMapping(),
                    DuplicateRecordCloning.Replace, false);

                // Collect what was added.
                using (var tr = targetDb.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(targetMsId, OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (!preExisting.Contains(id))
                            result.Add(id);
                    }
                    tr.Commit();
                }
            }

            return result;
        }

        /// <summary>
        /// Sweeps the profile cross-section along the curve identified by
        /// <paramref name="pathCurveId"/> using Solid3d.ExtrudeAlongPath.
        /// The cross-section region is cloned from the profile side-DB into targetDb
        /// so that the region, path curve, and solid all live in the same database
        /// during the sweep — which is required by the ObjectARX modeler.
        /// </summary>
        private static List<ObjectId> CreateExtrudedProfileAlongPath(
            string profileFilePath, ObjectId pathCurveId, Database targetDb,
            double rollAngleRad = 0.0)
        {
            var result = new List<ObjectId>();

            // --- Step 1: build the cross-section region in a side DB and clone it
            //             into targetDb so the sweep can reference both in one transaction.
            ObjectId targetMsId;
            using (var tr = targetDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                targetMsId = bt[BlockTableRecord.ModelSpace];
                tr.Commit();
            }

            ObjectId clonedRegionId = ObjectId.Null;

            using (var sideDb = new Database(false, true))
            {
                sideDb.ReadDwgFile(profileFilePath, FileShare.ReadWrite, true, "");
                sideDb.CloseInput(true);

                ObjectId regionObjId = ObjectId.Null;
                using (var tr = sideDb.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(sideDb.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var curves = new DBObjectCollection();
                    Region existingRegion = null;

                    foreach (ObjectId id in ms)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead);
                        if (ent is Region r) { existingRegion = r; break; }
                        if (ent is Curve c) curves.Add(c);
                    }

                    Region regionToExtrude = existingRegion;
                    if (regionToExtrude == null && curves.Count > 0)
                    {
                        var created = Region.CreateFromCurves(curves);
                        if (created.Count > 0)
                        {
                            var primary = (Region)created[0];
                            ms.AppendEntity(primary);
                            tr.AddNewlyCreatedDBObject(primary, true);
                            for (int i = 1; i < created.Count; i++)
                            {
                                var extra = (Region)created[i];
                                try { primary.BooleanOperation(BooleanOperationType.BoolUnite, extra); }
                                catch { }
                                extra.Dispose();
                            }
                            regionToExtrude = primary;
                        }
                    }

                    if (regionToExtrude != null)
                        regionObjId = regionToExtrude.ObjectId;

                    tr.Commit();
                }

                if (regionObjId == ObjectId.Null) return result;

                var idsToClone = new ObjectIdCollection { regionObjId };
                var idMap = new IdMapping();
                sideDb.WblockCloneObjects(idsToClone, targetMsId, idMap,
                    DuplicateRecordCloning.Replace, false);

                if (idMap.Contains(regionObjId))
                    clonedRegionId = idMap[regionObjId].Value;
            }

            if (clonedRegionId == ObjectId.Null) return result;

            // --- Step 2: orient the region at the path start and sweep — all inside targetDb.
            using (var tr = targetDb.TransactionManager.StartTransaction())
            {
                var pathCurve = tr.GetObject(pathCurveId, OpenMode.ForRead) as Curve;
                if (pathCurve == null) { tr.Abort(); return result; }

                double startParam = pathCurve.StartParam;
                Point3d startPt   = pathCurve.GetPointAtParameter(startParam);
                Vector3d deriv    = pathCurve.GetFirstDerivative(startParam);
                if (deriv.Length < 1e-10) { tr.Abort(); return result; }
                Vector3d startTangent = deriv.GetNormal();

                var region = tr.GetObject(clonedRegionId, OpenMode.ForWrite) as Region;
                if (region == null) { tr.Abort(); return result; }

                var xform = BuildAlignmentTransform(Vector3d.ZAxis, startTangent, startPt);
                region.TransformBy(xform);

                if (rollAngleRad != 0.0)
                    region.TransformBy(Matrix3d.Rotation(rollAngleRad, startTangent, startPt));

                var ms = (BlockTableRecord)tr.GetObject(targetMsId, OpenMode.ForWrite);

                var solid = new Solid3d();
                solid.ExtrudeAlongPath(region, pathCurve, 0.0);
                ms.AppendEntity(solid);
                tr.AddNewlyCreatedDBObject(solid, true);
                result.Add(solid.ObjectId);

                region.Erase();
                tr.Commit();
            }

            return result;
        }

        // ====================================================================
        // MCCreateProfilePlot
        // ====================================================================

        /// <summary>
        /// MCCreateProfilePlot - Generates a profile-plot process sheet for a chosen
        /// cross-section profile.  The sheet contains a front elevation, plan view and
        /// end (cross-section) view, all scaled to fit on a 2000 × 1500 mm sheet while
        /// showing true-value dimensions.
        /// </summary>
        [CommandMethod("MCCreateProfilePlot")]
        public void CreateProfilePlot()
        {
            var doc    = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            if (DatabaseService.CurrentProject == null)
            { editor.WriteMessage("\nNo project open. Use MCOpenProject first."); return; }

            var profileDrawings = MaterialLibraryService.GetProfileDrawings();
            if (profileDrawings.Count == 0)
            { editor.WriteMessage("\nNo profile drawings found. Create a Profile-type drawing first."); return; }

            // --- Optional: pre-fill length from a selected solid ---
            double suggestedLength = 0;
            var solidOpts = new PromptEntityOptions(
                "\nSelect a profile solid to read its length (or press Enter to enter manually): ");
            solidOpts.SetRejectMessage("\nSelect a 3D Solid or press Enter.");
            solidOpts.AddAllowedClass(typeof(Solid3d), true);
            solidOpts.AllowNone = true;

            var solidResult = editor.GetEntity(solidOpts);
            if (solidResult.Status == PromptStatus.OK)
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var solid = tr.GetObject(solidResult.ObjectId, OpenMode.ForRead) as Solid3d;
                    if (solid != null)
                    {
                        var ext = solid.GeometricExtents;
                        double dx = ext.MaxPoint.X - ext.MinPoint.X;
                        double dy = ext.MaxPoint.Y - ext.MinPoint.Y;
                        double dz = ext.MaxPoint.Z - ext.MinPoint.Z;
                        suggestedLength = Math.Max(dx, Math.Max(dy, dz));
                    }
                    tr.Commit();
                }
            }
            else if (solidResult.Status == PromptStatus.Cancel)
            { editor.WriteMessage("\nCommand cancelled."); return; }

            // --- Dialog ---
            var dialog = new ProfilePlotDialog(profileDrawings, suggestedLength);
            if (Application.ShowModalWindow(dialog) != true || dialog.SelectedDrawing == null)
            { editor.WriteMessage("\nCommand cancelled."); return; }

            var profileDwg = dialog.SelectedDrawing;
            double length  = dialog.Length;
            string plotName = dialog.PlotName;

            if (!File.Exists(profileDwg.FilePath))
            { editor.WriteMessage($"\nProfile file not found: {profileDwg.FilePath}"); return; }

            // --- Output path (unique) ---
            string outputDir = Path.Combine(
                DatabaseService.CurrentProject.Directory, "03 Fabrication", "Profile Plots");
            string baseName  = plotName.Replace(" ", "_").Replace("/", "-");
            string outputPath = Path.Combine(outputDir, baseName + ".dwg");
            if (File.Exists(outputPath))
            {
                int counter = 1;
                while (File.Exists(Path.Combine(outputDir, $"{baseName}_{counter}.dwg")))
                    counter++;
                outputPath = Path.Combine(outputDir, $"{baseName}_{counter}.dwg");
            }

            // --- Find template ---
            string templatePath = null;
            if (!string.IsNullOrEmpty(DatabaseService.CurrentProject.Directory))
            {
                string candidate = Path.Combine(DatabaseService.CurrentProject.Directory,
                    ProjectFolderLayout.Templates, "PROFILE_PLOTS_TEMPLATE.dwg");
                if (File.Exists(candidate))
                {
                    templatePath = candidate;
                    editor.WriteMessage("\nUsing PROFILE_PLOTS_TEMPLATE.dwg for styling.");
                }
                else
                {
                    editor.WriteMessage(
                        "\nNo PROFILE_PLOTS_TEMPLATE.dwg found in Templates folder — creating blank plot.");
                }
            }

            // --- Generate ---
            try
            {
                editor.WriteMessage($"\nGenerating profile plot for '{profileDwg.Name}' …");
                ProfilePlotService.CreatePlot(profileDwg.FilePath, length, outputPath, plotName, templatePath);
                editor.WriteMessage($"\nProfile plot saved: {outputPath}");

                // Register in the database so the Navigator can find it.
                try
                {
                    string plotDrawingName = Path.GetFileNameWithoutExtension(outputPath);
                    DatabaseService.CreateDrawing(
                        Guid.NewGuid(),
                        plotDrawingName,
                        DrawingTypes.ProfilePlot,
                        discipline: null,
                        filePath: outputPath,
                        description: $"Profile plot: {profileDwg.Name}, L={length:F0} mm");
                }
                catch (System.Exception dbEx)
                {
                    editor.WriteMessage($"\nNote: plot saved but DB register failed: {dbEx.Message}");
                }

                // Open the result
                Application.DocumentManager.Open(outputPath, false);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nError generating plot: {ex.Message}");
            }
        }

        [CommandMethod("MCExportToJson")]
        public void ExportToJson()
        {
            var editor = Application.DocumentManager.MdiActiveDocument.Editor;

            if (DatabaseService.CurrentProject == null)
            {
                editor.WriteMessage("\nNo project open. Use MCOpenProject first.");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Project to JSON",
                Filter = "Project Files (*.json)|*.json|All Files (*.*)|*.*",
                FileName = "pro.json",
                InitialDirectory = DatabaseService.CurrentProject.Directory
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                editor.WriteMessage("\nExporting project data...");
                JsonMigrationService.ExportCurrentProjectToJson(dlg.FileName);
                editor.WriteMessage($"\nExport complete: {dlg.FileName}");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nExport failed: {ex.Message}");
            }
        }

        [CommandMethod("MCImportFromJson")]
        public void ImportFromJson()
        {
            var editor = Application.DocumentManager.MdiActiveDocument.Editor;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Project from JSON",
                Filter = "Project Files (*.json)|*.json|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() != true) return;

            // Ask for the target connection string
            string cs = StorageRouter.GetNpgsqlConnectionString();
            if (string.IsNullOrEmpty(cs))
            {
                editor.WriteMessage("\nNo database connection set. Use MCConfigDatabase first, then retry.");
                return;
            }

            try
            {
                editor.WriteMessage("\nImporting project data into database...");
                JsonMigrationService.ImportFromJson(dlg.FileName, cs);
                editor.WriteMessage("\nImport complete. Use MCOpenProject to open the imported project.");
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nImport failed: {ex.Message}");
            }
        }

        // ==================== NEST MANAGER ====================

        /// <summary>
        /// MCNestManager - Open the Nest Manager to review, export (Send to Cut),
        /// and track cut status for all nesting runs in the current project.
        /// </summary>
        [CommandMethod("MCNestManager", CommandFlags.Session)]
        public void NestManager()
        {
            var doc    = Application.DocumentManager.MdiActiveDocument;
            var editor = doc.Editor;

            if (DatabaseService.CurrentProject == null)
            {
                editor.WriteMessage("\nNo project open. Use MCOpenProject first.");
                return;
            }

            var window = new NestManagerWindow();
            Application.ShowModalWindow(window);

            if (!string.IsNullOrEmpty(window.RequestedOpenPath) && File.Exists(window.RequestedOpenPath))
            {
                try { Application.DocumentManager.Open(window.RequestedOpenPath, false); }
                catch (System.Exception ex) { editor.WriteMessage($"\nCould not open drawing: {ex.Message}"); }
            }
        }
    }
}
