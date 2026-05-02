using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using System;
using System.Windows.Forms;

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
                _paletteSet.MinimumSize = new DrawingSize(280, 350);
                _paletteSet.Size = new DrawingSize(320, 500);
                _paletteSet.DockEnabled = DockSides.Left | DockSides.Right;

                _control = new MetadataPaletteControl();
                _paletteSet.Add("Properties", _control);

                _paletteSet.StateChanged += (s, e) =>
                {
                    if (_paletteSet.Visible)
                        UpdateMetadataDisplay();
                };
            }

            _paletteSet.Visible = true;
            UpdateMetadataDisplay();
        }

        public static void Hide()
        {
            if (_paletteSet != null) _paletteSet.Visible = false;
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

                if (!(entity is Solid3d solid))
                {
                    _control.ShowNotASolid(entity.GetType().Name);
                    return;
                }

                string partName = "";
                string materialName = "";
                string handle = entity.Handle.ToString();

                var xdata = entity.GetXDataForApplication(Commands.AppName);
                if (xdata != null)
                {
                    var values = xdata.AsArray();
                    for (int i = 1; i + 1 < values.Length; i += 2)
                    {
                        string key = values[i].Value.ToString();
                        string val = values[i + 1].Value.ToString();
                        if (key == "PartName") partName = val;
                        else if (key == "MaterialName") materialName = val;
                    }
                }

                var extents = solid.GeometricExtents;
                double width  = extents.MaxPoint.X - extents.MinPoint.X;
                double depth  = extents.MaxPoint.Y - extents.MinPoint.Y;
                double height = extents.MaxPoint.Z - extents.MinPoint.Z;
                string layer  = solid.Layer;

                _control.ShowMetadata(partName, materialName, handle, objectId, layer, width, depth, height);
                tr.Commit();
            }
        }
    }

    public class MetadataPaletteControl : UserControl
    {
        // AutoCAD dark theme colours
        public static readonly DrawingColor BgColor        = DrawingColor.FromArgb(45,  45,  48);
        public static readonly DrawingColor HeaderBgColor  = DrawingColor.FromArgb(62,  62,  66);
        public static readonly DrawingColor HeaderTextColor= DrawingColor.FromArgb(220, 220, 220);
        public static readonly DrawingColor LabelColor     = DrawingColor.FromArgb(180, 180, 180);
        public static readonly DrawingColor ValueColor     = DrawingColor.FromArgb(255, 255, 255);
        public static readonly DrawingColor BorderColor    = DrawingColor.FromArgb(67,  67,  70);
        public static readonly DrawingColor InputBgColor   = DrawingColor.FromArgb(37,  37,  38);
        public static readonly DrawingColor RowAltColor    = DrawingColor.FromArgb(50,  50,  53);

        private ObjectId _currentObjectId;
        private Panel _mainContainer;
        private ProjectSection _projectSection;
        private PropertiesSection _propsSection;

        public MetadataPaletteControl()
        {
            BackColor = BgColor;
            Dock = DockStyle.Fill;
            Padding = new Padding(0);
            BuildUI();
        }

        private void BuildUI()
        {
            // Plain Panel with Dock=Fill so children get correct width immediately
            // (FlowLayoutPanel ignores Dock on children and never propagates width,
            // causing all text to be invisible at zero-width).
            _mainContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BgColor,
                AutoScroll = true,
                Padding = new Padding(0),
            };

            _projectSection = new ProjectSection();
            _projectSection.Dock = DockStyle.Top;

            _propsSection = new PropertiesSection();
            _propsSection.Dock = DockStyle.Top;

            // Last-added control docks to top first with Dock=Top stacking.
            _mainContainer.Controls.Add(_propsSection);    // index 0 → docked last  → bottom
            _mainContainer.Controls.Add(_projectSection);  // index 1 → docked first → top

            Controls.Add(_mainContainer);
        }

        public void UpdateProjectInfo() => _projectSection?.UpdateProjectInfo();

        public void ShowNoSelection()
        {
            _propsSection?.ShowMessage("Select a 3D solid");
            _currentObjectId = ObjectId.Null;
        }

        public void ShowNotASolid(string typeName)
        {
            _propsSection?.ShowMessage($"Selected: {typeName}\n(Only 3D solids supported)");
            _currentObjectId = ObjectId.Null;
        }

        public void ShowMetadata(string partName, string materialName, string handle,
            ObjectId objectId, string layer, double width, double depth, double height)
        {
            _currentObjectId = objectId;
            _propsSection.ShowProperties(partName, materialName, handle, layer, width, depth, height);
            _propsSection.OnApply = () => OnApplyClick();
        }

        private void OnApplyClick()
        {
            if (_currentObjectId == ObjectId.Null)
            {
                MessageBox.Show("Select a 3D solid first.", "No Selection",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string name = _propsSection.GetPartName().Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Enter a part name.", "Empty",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (PartPropertiesManager.SetPartName(_currentObjectId, name))
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage($"\nPart name set: {name}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reusable section header bar
    // ─────────────────────────────────────────────────────────────────────────
    public class SectionHeader : Panel
    {
        public SectionHeader(string title)
        {
            Height = 24;
            Dock = DockStyle.Top;
            BackColor = MetadataPaletteControl.HeaderBgColor;

            Controls.Add(new Label
            {
                Text = "▼",
                Location = new DrawingPoint(6, 4),
                Size = new DrawingSize(16, 16),
                ForeColor = MetadataPaletteControl.HeaderTextColor,
                Font = new DrawingFont("Segoe UI", 8),
                BackColor = MetadataPaletteControl.HeaderBgColor,
            });

            Controls.Add(new Label
            {
                Text = title,
                Location = new DrawingPoint(24, 4),
                AutoSize = true,
                ForeColor = MetadataPaletteControl.HeaderTextColor,
                Font = new DrawingFont("Segoe UI", 9, DrawingFontStyle.Bold),
                BackColor = MetadataPaletteControl.HeaderBgColor,
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A property row: fixed-width label on the left, value on the right.
    // Uses a TableLayoutPanel internally so layout is unambiguous regardless
    // of the WinForms docking-order assumptions.
    // ─────────────────────────────────────────────────────────────────────────
    public class PropertyRow : Panel
    {
        private Label _valueLabel;
        private TextBox _valueTextBox;
        private readonly bool _isEditable;

        public PropertyRow(string label, bool editable = false, bool altRow = false)
        {
            Height = 24;
            Dock = DockStyle.Top;
            BackColor = altRow ? MetadataPaletteControl.RowAltColor : MetadataPaletteControl.BgColor;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = BackColor,
                Padding = new Padding(0),
                Margin = new Padding(0),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var labelCtrl = new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                ForeColor = MetadataPaletteControl.LabelColor,
                Font = new DrawingFont("Segoe UI", 9),
                BackColor = BackColor,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
            };
            table.Controls.Add(labelCtrl, 0, 0);

            _isEditable = editable;
            if (editable)
            {
                _valueTextBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = MetadataPaletteControl.InputBgColor,
                    ForeColor = MetadataPaletteControl.ValueColor,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new DrawingFont("Segoe UI", 9),
                };
                table.Controls.Add(_valueTextBox, 1, 0);
            }
            else
            {
                _valueLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = MetadataPaletteControl.ValueColor,
                    Font = new DrawingFont("Segoe UI", 9),
                    BackColor = BackColor,
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                    Padding = new Padding(4, 0, 4, 0),
                };
                table.Controls.Add(_valueLabel, 1, 0);
            }

            Controls.Add(table);
        }

        public void SetValue(string value)
        {
            if (_isEditable) _valueTextBox.Text = value;
            else _valueLabel.Text = value;
        }

        public string GetValue() => _isEditable ? _valueTextBox.Text : _valueLabel.Text;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Project section: shows active project name + quick-action buttons.
    // ─────────────────────────────────────────────────────────────────────────
    public class ProjectSection : Panel
    {
        private PropertyRow _projectRow;
        private Button _openBtn;
        private Button _saveBtn;

        public ProjectSection()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = MetadataPaletteControl.BgColor;
            Padding = new Padding(0);
            Margin = new Padding(0);
            BuildUI();
        }

        private void BuildUI()
        {
            // Build from top to bottom: header → project row → button panel.
            // With Dock=Top stacking, last-added = topmost, so we add in reverse.
            var buttonPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = MetadataPaletteControl.BgColor,
                Padding = new Padding(8, 4, 8, 4),
            };

            _openBtn = MakeButton("Open Project", 0, 4, 115);
            _openBtn.Click += OnOpenProject;
            buttonPanel.Controls.Add(_openBtn);

            _saveBtn = MakeButton("Save Parts", 121, 4, 95);
            _saveBtn.BackColor = DrawingColor.FromArgb(40, 120, 40);
            _saveBtn.Enabled = false;
            _saveBtn.Click += OnSaveAll;
            buttonPanel.Controls.Add(_saveBtn);

            _projectRow = new PropertyRow("Project", false);
            _projectRow.SetValue("None");

            var header = new SectionHeader("Project");

            // Reverse order: header last → topmost.
            Controls.Add(buttonPanel);
            Controls.Add(_projectRow);
            Controls.Add(header);
        }

        private Button MakeButton(string text, int x, int y, int width)
        {
            var btn = new Button
            {
                Text = text,
                Location = new DrawingPoint(x, y),
                Size = new DrawingSize(width, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = DrawingColor.FromArgb(70, 70, 75),
                ForeColor = MetadataPaletteControl.ValueColor,
                Font = new DrawingFont("Segoe UI", 8.5f),
                Cursor = Cursors.Hand,
            };
            btn.FlatAppearance.BorderColor = MetadataPaletteControl.BorderColor;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = DrawingColor.FromArgb(90, 90, 95);
            return btn;
        }

        public void UpdateProjectInfo()
        {
            if (DatabaseService.CurrentProject != null)
            {
                _projectRow.SetValue(DatabaseService.CurrentProject.Name);
                _saveBtn.Enabled = true;
            }
            else
            {
                _projectRow.SetValue("None");
                _saveBtn.Enabled = false;
            }
        }

        private void OnOpenProject(object sender, EventArgs e)
        {
            try
            {
                var dialog = new ProjectDialog();
                var result = AcadApp.ShowModalWindow(dialog);
                if (result == true && dialog.SelectedProject != null)
                {
                    DatabaseService.SetCurrentProject(dialog.SelectedProject);
                    UpdateProjectInfo();
                    AcadApp.DocumentManager.MdiActiveDocument?
                        .Editor.WriteMessage($"\nProject opened: {dialog.SelectedProject.Name}");
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSaveAll(object sender, EventArgs e)
        {
            if (DatabaseService.CurrentProject == null)
            {
                MessageBox.Show("Open a project first.", "No Project",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                int count = PartPropertiesManager.SaveAllPartsToDatabase();
                MessageBox.Show($"Saved {count} part(s).", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Properties section: shows metadata for the selected 3D solid.
    // ─────────────────────────────────────────────────────────────────────────
    public class PropertiesSection : Panel
    {
        private Label _messageLabel;
        private Panel _propertiesContent;
        private PropertyRow _handleRow;
        private PropertyRow _layerRow;
        private PropertyRow _widthRow;
        private PropertyRow _depthRow;
        private PropertyRow _heightRow;
        private PropertyRow _materialRow;
        private PropertyRow _partNameRow;
        private Button _applyBtn;

        public Action OnApply { get; set; }

        public PropertiesSection()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = MetadataPaletteControl.BgColor;
            Padding = new Padding(0);
            Margin = new Padding(0);
            BuildUI();
        }

        private void BuildUI()
        {
            // Properties content panel (hidden until a valid solid is selected)
            _propertiesContent = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = MetadataPaletteControl.BgColor,
                Padding = new Padding(0),
                Visible = false,
            };

            // Apply button row
            var applyPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = MetadataPaletteControl.BgColor,
                Padding = new Padding(8, 4, 8, 4),
            };
            _applyBtn = new Button
            {
                Text = "Apply",
                Location = new DrawingPoint(0, 4),
                Size = new DrawingSize(80, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = DrawingColor.FromArgb(0, 122, 204),
                ForeColor = MetadataPaletteControl.ValueColor,
                Font = new DrawingFont("Segoe UI", 8.5f, DrawingFontStyle.Bold),
                Cursor = Cursors.Hand,
            };
            _applyBtn.FlatAppearance.BorderSize = 0;
            _applyBtn.FlatAppearance.MouseOverBackColor = DrawingColor.FromArgb(0, 142, 224);
            _applyBtn.Click += (s, e) => OnApply?.Invoke();
            applyPanel.Controls.Add(_applyBtn);

            // Property rows (will be stacked top-to-bottom in reverse-add order)
            _partNameRow = new PropertyRow("Part Name", editable: true);
            _materialRow = new PropertyRow("Material",  editable: false, altRow: true);

            var sep = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = MetadataPaletteControl.BgColor };

            _heightRow = new PropertyRow("Height Z (mm)", altRow: true);
            _depthRow  = new PropertyRow("Depth  Y (mm)");
            _widthRow  = new PropertyRow("Width  X (mm)", altRow: true);

            var sep2 = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = MetadataPaletteControl.BgColor };

            _layerRow  = new PropertyRow("Layer",  altRow: true);
            _handleRow = new PropertyRow("Handle");

            // Add in reverse order (last added = topmost with Dock=Top stacking)
            _propertiesContent.Controls.Add(applyPanel);
            _propertiesContent.Controls.Add(_partNameRow);
            _propertiesContent.Controls.Add(_materialRow);
            _propertiesContent.Controls.Add(sep);
            _propertiesContent.Controls.Add(_heightRow);
            _propertiesContent.Controls.Add(_depthRow);
            _propertiesContent.Controls.Add(_widthRow);
            _propertiesContent.Controls.Add(sep2);
            _propertiesContent.Controls.Add(_layerRow);
            _propertiesContent.Controls.Add(_handleRow);

            // Message label shown when nothing is selected
            _messageLabel = new Label
            {
                Text = "Select a 3D solid",
                Dock = DockStyle.Top,
                Height = 32,
                ForeColor = MetadataPaletteControl.LabelColor,
                BackColor = MetadataPaletteControl.BgColor,
                Font = new DrawingFont("Segoe UI", 9),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
            };

            var header = new SectionHeader("MC Part Properties");

            // Reverse order: header last → topmost.
            Controls.Add(_propertiesContent);
            Controls.Add(_messageLabel);
            Controls.Add(header);
        }

        public void ShowMessage(string message)
        {
            _messageLabel.Text = message;
            _messageLabel.Visible = true;
            _propertiesContent.Visible = false;
        }

        public void ShowProperties(string partName, string materialName, string handle,
            string layer, double width, double depth, double height)
        {
            _messageLabel.Visible = false;
            _propertiesContent.Visible = true;

            _handleRow.SetValue(handle);
            _layerRow.SetValue(layer);
            _widthRow.SetValue($"{width:F2}");
            _depthRow.SetValue($"{depth:F2}");
            _heightRow.SetValue($"{height:F2}");
            _materialRow.SetValue(string.IsNullOrEmpty(materialName) ? "—" : materialName);
            _partNameRow.SetValue(partName);
        }

        public string GetPartName() => _partNameRow.GetValue();
    }
}
