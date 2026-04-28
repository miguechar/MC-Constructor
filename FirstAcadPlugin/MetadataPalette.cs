using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using System;
using System.Windows.Forms;

// Aliases
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingColor = System.Drawing.Color;
using DrawingSize = System.Drawing.Size;
using DrawingPoint = System.Drawing.Point;

namespace FirstAcadPlugin
{
    public static class MetadataPalette
    {
        private static PaletteSet _paletteSet;
        private static MetadataPaletteControl _control;
        private static bool _eventsSubscribed = false;

        public static void Initialize()
        {
            if (!_eventsSubscribed)
            {
                AcadApp.DocumentManager.DocumentActivated += OnDocumentActivated;
                SubscribeToActiveDocument();
                _eventsSubscribed = true;
            }
        }

        public static void Show()
        {
            if (_paletteSet == null)
            {
                _paletteSet = new PaletteSet("MC Part Properties", new Guid("7E35B5E7-1B3A-4F2C-9D8E-ABC123456789"));
                _paletteSet.Style = PaletteSetStyles.ShowPropertiesMenu
                                  | PaletteSetStyles.ShowAutoHideButton
                                  | PaletteSetStyles.ShowCloseButton;
                _paletteSet.MinimumSize = new DrawingSize(220, 350);
                _paletteSet.Size = new DrawingSize(280, 400);
                _paletteSet.DockEnabled = DockSides.Left | DockSides.Right;

                _control = new MetadataPaletteControl();
                _paletteSet.Add("Properties", _control);

                _paletteSet.StateChanged += (s, e) =>
                {
                    if (_paletteSet.Visible)
                        UpdateMetadataDisplay();
                };

                _paletteSet.SizeChanged += (s, e) =>
                {
                    _control?.HandleResize();
                };
            }

            _paletteSet.Visible = true;
            UpdateMetadataDisplay();
        }

        public static void Hide()
        {
            if (_paletteSet != null)
                _paletteSet.Visible = false;
        }
        public static void Toggle()
        {
            if (_paletteSet == null || !_paletteSet.Visible) Show();
            else _paletteSet.Visible = false;
        }

        public static void RefreshProjectInfo() => _control?.UpdateProjectInfo();

        private static void SubscribeToActiveDocument()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null) doc.ImpliedSelectionChanged += OnSelectionChanged;
        }

        private static void OnDocumentActivated(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document != null)
            {
                e.Document.ImpliedSelectionChanged += OnSelectionChanged;
                UpdateMetadataDisplay();
            }
        }

        private static void OnSelectionChanged(object sender, EventArgs e) => UpdateMetadataDisplay();

        private static void UpdateMetadataDisplay()
        {
            if (_control == null || _paletteSet == null || !_paletteSet.Visible) return;

            _control.UpdateProjectInfo();

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { _control.ShowNoSelection(); return; }

            var selection = doc.Editor.SelectImplied();
            if (selection.Status != PromptStatus.OK || selection.Value.Count == 0)
            {
                _control.ShowNoSelection();
                return;
            }

            var objectId = selection.Value[0].ObjectId;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(objectId, OpenMode.ForRead) as Entity;
                if (entity == null) { _control.ShowNoSelection(); return; }

                if (!(entity is Solid3d))
                {
                    _control.ShowNotASolid(entity.GetType().Name);
                    return;
                }

                var xdata = entity.GetXDataForApplication(Commands.AppName);
                string partName = "";
                string handle = entity.Handle.ToString();

                if (xdata != null)
                {
                    var values = xdata.AsArray();
                    for (int i = 1; i < values.Length - 1; i += 2)
                    {
                        if (values[i].Value.ToString() == "PartName")
                            partName = values[i + 1].Value.ToString();
                    }
                }

                _control.ShowMetadata(partName, handle, objectId);
                tr.Commit();
            }
        }
    }

    public class MetadataPaletteControl : UserControl
    {
        private Label projectLabel;
        private Label projectValueLabel;
        private Button openProjectBtn;
        private Button saveAllBtn;
        private Label statusLabel;
        private Panel propsPanel;
        private Label handleLabel;
        private Label handleValueLabel;
        private Label partNameLabel;
        private TextBox partNameBox;
        private Button applyBtn;

        private ObjectId _currentObjectId;

        public MetadataPaletteControl()
        {
            this.BackColor = DrawingColor.FromArgb(240, 240, 240);
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;
            this.Padding = new Padding(6);

            BuildUI();
        }

        private void BuildUI()
        {
            int y = 8;
            int margin = 8;

            // === PROJECT SECTION ===
            var projHeader = MakeLabel("PROJECT", margin, y, true, DrawingColor.FromArgb(0, 120, 200));
            this.Controls.Add(projHeader);
            y += 22;

            projectLabel = MakeLabel("Current:", margin, y, false, DrawingColor.Gray);
            this.Controls.Add(projectLabel);

            projectValueLabel = MakeLabel("None", margin + 55, y, false, DrawingColor.Black);
            this.Controls.Add(projectValueLabel);
            y += 24;

            openProjectBtn = MakeButton("Open Project", margin, y, 100, OnOpenProject);
            this.Controls.Add(openProjectBtn);

            saveAllBtn = MakeButton("Save Parts", margin + 108, y, 90, OnSaveAll);
            saveAllBtn.BackColor = DrawingColor.FromArgb(40, 160, 70);
            saveAllBtn.Enabled = false;
            this.Controls.Add(saveAllBtn);
            y += 40;

            // Separator
            var sep = new Panel { Location = new DrawingPoint(margin, y), Size = new DrawingSize(200, 1), BackColor = DrawingColor.LightGray };
            this.Controls.Add(sep);
            y += 10;

            // === PROPERTIES SECTION ===
            var propsHeader = MakeLabel("PART PROPERTIES", margin, y, true, DrawingColor.FromArgb(0, 120, 200));
            this.Controls.Add(propsHeader);
            y += 24;

            statusLabel = MakeLabel("Select a 3D solid", margin, y, false, DrawingColor.Gray);
            this.Controls.Add(statusLabel);

            propsPanel = new Panel { Location = new DrawingPoint(margin, y), Size = new DrawingSize(200, 120), Visible = false };
            this.Controls.Add(propsPanel);

            // Inside props panel
            handleLabel = MakeLabel("Handle:", 0, 0, false, DrawingColor.Gray);
            propsPanel.Controls.Add(handleLabel);

            handleValueLabel = MakeLabel("", 60, 0, true, DrawingColor.Black);
            propsPanel.Controls.Add(handleValueLabel);

            partNameLabel = MakeLabel("Part Name:", 0, 28, false, DrawingColor.Gray);
            propsPanel.Controls.Add(partNameLabel);

            partNameBox = new TextBox
            {
                Location = new DrawingPoint(0, 48),
                Size = new DrawingSize(180, 24),
                Font = new DrawingFont("Segoe UI", 9)
            };
            propsPanel.Controls.Add(partNameBox);

            applyBtn = MakeButton("Apply", 0, 80, 80, OnApply);
            propsPanel.Controls.Add(applyBtn);
        }

        private Label MakeLabel(string text, int x, int y, bool bold, DrawingColor color)
        {
            return new Label
            {
                Text = text,
                Location = new DrawingPoint(x, y),
                AutoSize = true,
                Font = new DrawingFont("Segoe UI", 9, bold ? DrawingFontStyle.Bold : DrawingFontStyle.Regular),
                ForeColor = color
            };
        }

        private Button MakeButton(string text, int x, int y, int width, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = text,
                Location = new DrawingPoint(x, y),
                Size = new DrawingSize(width, 28),
                Font = new DrawingFont("Segoe UI", 8.5f),
                BackColor = DrawingColor.FromArgb(0, 120, 200),
                ForeColor = DrawingColor.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += onClick;
            return btn;
        }

        public void HandleResize()
        {
            int w = this.Width - 20;
            if (w < 100) w = 100;

            partNameBox.Width = w - 10;
            saveAllBtn.Left = openProjectBtn.Right + 8;
        }

        public void UpdateProjectInfo()
        {
            if (DatabaseService.CurrentProject != null)
            {
                projectValueLabel.Text = DatabaseService.CurrentProject.Name;
                projectValueLabel.ForeColor = DrawingColor.Black;
                saveAllBtn.Enabled = true;
            }
            else
            {
                projectValueLabel.Text = "None";
                projectValueLabel.ForeColor = DrawingColor.Gray;
                saveAllBtn.Enabled = false;
            }
        }

        public void ShowNoSelection()
        {
            statusLabel.Text = "Select a 3D solid to view properties";
            statusLabel.Visible = true;
            propsPanel.Visible = false;
            _currentObjectId = ObjectId.Null;
        }

        public void ShowNotASolid(string typeName)
        {
            statusLabel.Text = $"Selected: {typeName}\n(Only 3D solids supported)";
            statusLabel.Visible = true;
            propsPanel.Visible = false;
            _currentObjectId = ObjectId.Null;
        }

        public void ShowMetadata(string partName, string handle, ObjectId objectId)
        {
            statusLabel.Visible = false;
            propsPanel.Visible = true;
            handleValueLabel.Text = handle;
            partNameBox.Text = partName;
            _currentObjectId = objectId;

            partNameBox.BackColor = string.IsNullOrEmpty(partName)
                ? DrawingColor.FromArgb(255, 255, 220)
                : DrawingColor.White;
        }

        private void OnOpenProject(object sender, EventArgs e)
        {
            // Show the project dialog directly
            try
            {
                var dialog = new ProjectDialog();
                var result = AcadApp.ShowModalWindow(dialog);

                if (result == true && dialog.SelectedProject != null)
                {
                    DatabaseService.SetCurrentProject(dialog.SelectedProject);
                    UpdateProjectInfo();

                    var doc = AcadApp.DocumentManager.MdiActiveDocument;
                    if (doc != null)
                    {
                        doc.Editor.WriteMessage($"\nProject opened: {dialog.SelectedProject.Name}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error opening project:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSaveAll(object sender, EventArgs e)
        {
            if (DatabaseService.CurrentProject == null)
            {
                MessageBox.Show("Please open a project first.", "No Project",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                int count = PartPropertiesManager.SaveAllPartsToDatabase();
                MessageBox.Show($"Saved {count} part(s) to database.\n\nProject: {DatabaseService.CurrentProject.Name}",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage($"\nSaved {count} part(s) to project: {DatabaseService.CurrentProject.Name}");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error saving:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnApply(object sender, EventArgs e)
        {
            if (_currentObjectId == ObjectId.Null)
            {
                MessageBox.Show("Select a 3D solid first.", "No Selection",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string name = partNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Enter a part name.", "Empty", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (PartPropertiesManager.SetPartName(_currentObjectId, name))
            {
                partNameBox.BackColor = DrawingColor.White;
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage($"\nPart name set: {name}");
            }
            else
            {
                MessageBox.Show("Failed to set part name.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
