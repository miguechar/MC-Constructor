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

                var extents = solid.GeometricExtents;
                double width = extents.MaxPoint.X - extents.MinPoint.X;
                double depth = extents.MaxPoint.Y - extents.MinPoint.Y;
                double height = extents.MaxPoint.Z - extents.MinPoint.Z;
                string layer = solid.Layer;

                _control.ShowMetadata(partName, handle, objectId, layer, width, depth, height);
                tr.Commit();
            }
        }
    }

    /// <summary>
    /// Custom palette control styled like AutoCAD's Properties palette.
    /// Uses Dock=Top for responsive layout.
    /// </summary>
    public class MetadataPaletteControl : UserControl
    {
        // AutoCAD dark theme colors
        public static readonly DrawingColor BgColor = DrawingColor.FromArgb(45, 45, 48);
        public static readonly DrawingColor HeaderBgColor = DrawingColor.FromArgb(62, 62, 66);
        public static readonly DrawingColor HeaderTextColor = DrawingColor.FromArgb(220, 220, 220);
        public static readonly DrawingColor LabelColor = DrawingColor.FromArgb(180, 180, 180);
        public static readonly DrawingColor ValueColor = DrawingColor.FromArgb(255, 255, 255);
        public static readonly DrawingColor BorderColor = DrawingColor.FromArgb(67, 67, 70);
        public static readonly DrawingColor InputBgColor = DrawingColor.FromArgb(37, 37, 38);
        public static readonly DrawingColor RowAltColor = DrawingColor.FromArgb(50, 50, 53);

        private ObjectId _currentObjectId;

        // Sections
        private FlowLayoutPanel mainContainer;
        private ProjectSection projectSection;
        private PropertiesSection propsSection;

        public MetadataPaletteControl()
        {
            this.BackColor = BgColor;
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(0);
            this.AutoScroll = true;
            BuildUI();
        }

        private void BuildUI()
        {
            mainContainer = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                BackColor = BgColor,
                FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            // Project section
            projectSection = new ProjectSection();
            mainContainer.Controls.Add(projectSection);

            // Properties section
            propsSection = new PropertiesSection();
            mainContainer.Controls.Add(propsSection);

            this.Controls.Add(mainContainer);

            // Make sections fill width
            this.Resize += OnResize;
            OnResize(null, null);
        }

        private void OnResize(object sender, EventArgs e)
        {
            int w = this.ClientSize.Width;
            if (w < 100) w = 100;
            if (mainContainer != null) mainContainer.Width = w;
            if (projectSection != null) projectSection.Width = w;
            if (propsSection != null) propsSection.Width = w;
        }

        public void UpdateProjectInfo() => projectSection?.UpdateProjectInfo();

        public void ShowNoSelection()
        {
            propsSection.ShowMessage("Select a 3D solid");
            _currentObjectId = ObjectId.Null;
        }

        public void ShowNotASolid(string typeName)
        {
            propsSection.ShowMessage($"Selected: {typeName}\n(Only 3D solids supported)");
            _currentObjectId = ObjectId.Null;
        }

        public void ShowMetadata(string partName, string handle, ObjectId objectId,
            string layer, double width, double depth, double height)
        {
            _currentObjectId = objectId;
            propsSection.ShowProperties(partName, handle, layer, width, depth, height);
            propsSection.OnApply = () => OnApplyClick();
        }

        private void OnApplyClick()
        {
            if (_currentObjectId == ObjectId.Null)
            {
                MessageBox.Show("Select a 3D solid first.", "No Selection",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string name = propsSection.GetPartName().Trim();
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

    /// <summary>
    /// A section header bar (like "Project" or "MC Part Properties").
    /// </summary>
    public class SectionHeader : Panel
    {
        public SectionHeader(string title)
        {
            this.Height = 24;
            this.Dock = DockStyle.Top;
            this.BackColor = MetadataPaletteControl.HeaderBgColor;

            var collapseLabel = new Label
            {
                Text = "▼",
                Location = new DrawingPoint(6, 4),
                Size = new DrawingSize(16, 16),
                ForeColor = MetadataPaletteControl.HeaderTextColor,
                Font = new DrawingFont("Segoe UI", 8),
                BackColor = MetadataPaletteControl.HeaderBgColor
            };
            this.Controls.Add(collapseLabel);

            var titleLabel = new Label
            {
                Text = title,
                Location = new DrawingPoint(24, 4),
                AutoSize = true,
                ForeColor = MetadataPaletteControl.HeaderTextColor,
                Font = new DrawingFont("Segoe UI", 9, DrawingFontStyle.Bold),
                BackColor = MetadataPaletteControl.HeaderBgColor
            };
            this.Controls.Add(titleLabel);
        }
    }

    /// <summary>
    /// A property row with label on left and value on right.
    /// </summary>
    public class PropertyRow : Panel
    {
        private Label valueLabel;
        private TextBox valueTextBox;
        private bool isEditable;

        public PropertyRow(string label, bool editable = false, bool altRow = false)
        {
            this.Height = 22;
            this.Dock = DockStyle.Top;
            this.BackColor = altRow ? MetadataPaletteControl.RowAltColor : MetadataPaletteControl.BgColor;

            var labelCtrl = new Label
            {
                Text = label,
                Dock = DockStyle.Left,
                Width = 100,
                ForeColor = MetadataPaletteControl.LabelColor,
                Font = new DrawingFont("Segoe UI", 9),
                BackColor = this.BackColor,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };
            this.Controls.Add(labelCtrl);

            isEditable = editable;
            if (editable)
            {
                valueTextBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = MetadataPaletteControl.InputBgColor,
                    ForeColor = MetadataPaletteControl.ValueColor,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new DrawingFont("Segoe UI", 9),
                    Margin = new Padding(0, 2, 4, 2)
                };
                this.Controls.Add(valueTextBox);
                valueTextBox.BringToFront();
            }
            else
            {
                valueLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = MetadataPaletteControl.ValueColor,
                    Font = new DrawingFont("Segoe UI", 9),
                    BackColor = this.BackColor,
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                    Padding = new Padding(4, 0, 4, 0)
                };
                this.Controls.Add(valueLabel);
                valueLabel.BringToFront();
            }
        }

        public void SetValue(string value)
        {
            if (isEditable) valueTextBox.Text = value;
            else valueLabel.Text = value;
        }

        public string GetValue() => isEditable ? valueTextBox.Text : valueLabel.Text;
    }

    /// <summary>
    /// Project section: shows current project name and action buttons.
    /// </summary>
    public class ProjectSection : Panel
    {
        private PropertyRow projectRow;
        private Button openBtn;
        private Button saveBtn;
        private Panel buttonPanel;

        public ProjectSection()
        {
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.BackColor = MetadataPaletteControl.BgColor;
            this.Padding = new Padding(0);
            this.Margin = new Padding(0);
            BuildUI();
        }

        private void BuildUI()
        {
            // Button panel goes at bottom (Dock=Top is reverse order, last added is on top)
            buttonPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = MetadataPaletteControl.BgColor,
                Padding = new Padding(8, 4, 8, 4)
            };

            openBtn = CreateButton("Open Project", 0, 4, 110);
            openBtn.Click += OnOpenProject;
            buttonPanel.Controls.Add(openBtn);

            saveBtn = CreateButton("Save Parts", 116, 4, 95);
            saveBtn.BackColor = DrawingColor.FromArgb(40, 120, 40);
            saveBtn.Enabled = false;
            saveBtn.Click += OnSaveAll;
            buttonPanel.Controls.Add(saveBtn);

            // Project row (shows current project)
            projectRow = new PropertyRow("Project", false);
            projectRow.SetValue("None");

            // Header
            var header = new SectionHeader("Project");

            // Add in reverse order for Dock=Top stacking
            this.Controls.Add(buttonPanel);
            this.Controls.Add(projectRow);
            this.Controls.Add(header);
        }

        private Button CreateButton(string text, int x, int y, int width)
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
                Cursor = Cursors.Hand
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
                projectRow.SetValue(DatabaseService.CurrentProject.Name);
                saveBtn.Enabled = true;
            }
            else
            {
                projectRow.SetValue("None");
                saveBtn.Enabled = false;
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

                    var doc = AcadApp.DocumentManager.MdiActiveDocument;
                    doc?.Editor.WriteMessage($"\nProject opened: {dialog.SelectedProject.Name}");
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

    /// <summary>
    /// MC Part Properties section: shows selected part details.
    /// </summary>
    public class PropertiesSection : Panel
    {
        private SectionHeader header;
        private Label messageLabel;
        private Panel propertiesContent;
        private PropertyRow handleRow;
        private PropertyRow layerRow;
        private PropertyRow widthRow;
        private PropertyRow depthRow;
        private PropertyRow heightRow;
        private PropertyRow partNameRow;
        private Button applyBtn;

        public Action OnApply { get; set; }

        public PropertiesSection()
        {
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.BackColor = MetadataPaletteControl.BgColor;
            this.Padding = new Padding(0);
            this.Margin = new Padding(0);
            BuildUI();
        }

        private void BuildUI()
        {
            // Build properties content panel (initially hidden)
            propertiesContent = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = MetadataPaletteControl.BgColor,
                Padding = new Padding(0),
                Visible = false
            };

            // Apply button panel
            var applyPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = MetadataPaletteControl.BgColor,
                Padding = new Padding(8, 4, 8, 4)
            };

            applyBtn = new Button
            {
                Text = "Apply",
                Location = new DrawingPoint(0, 4),
                Size = new DrawingSize(80, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = DrawingColor.FromArgb(0, 122, 204),
                ForeColor = MetadataPaletteControl.ValueColor,
                Font = new DrawingFont("Segoe UI", 8.5f, DrawingFontStyle.Bold),
                Cursor = Cursors.Hand
            };
            applyBtn.FlatAppearance.BorderSize = 0;
            applyBtn.FlatAppearance.MouseOverBackColor = DrawingColor.FromArgb(0, 142, 224);
            applyBtn.Click += (s, e) => OnApply?.Invoke();
            applyPanel.Controls.Add(applyBtn);

            // Part Name (editable)
            partNameRow = new PropertyRow("Part Name", true);

            // Separator
            var sep = new Panel
            {
                Dock = DockStyle.Top,
                Height = 6,
                BackColor = MetadataPaletteControl.BgColor
            };

            // Dimensions
            heightRow = new PropertyRow("Height (Z)", false, true);
            depthRow = new PropertyRow("Depth (Y)", false);
            widthRow = new PropertyRow("Width (X)", false, true);

            // Separator
            var sep2 = new Panel
            {
                Dock = DockStyle.Top,
                Height = 6,
                BackColor = MetadataPaletteControl.BgColor
            };

            // General
            layerRow = new PropertyRow("Layer", false, true);
            handleRow = new PropertyRow("Handle", false);

            // Stack in reverse for Dock=Top (last added shows first)
            propertiesContent.Controls.Add(applyPanel);
            propertiesContent.Controls.Add(partNameRow);
            propertiesContent.Controls.Add(sep);
            propertiesContent.Controls.Add(heightRow);
            propertiesContent.Controls.Add(depthRow);
            propertiesContent.Controls.Add(widthRow);
            propertiesContent.Controls.Add(sep2);
            propertiesContent.Controls.Add(layerRow);
            propertiesContent.Controls.Add(handleRow);

            // Message label (shown when no selection)
            messageLabel = new Label
            {
                Text = "Select a 3D solid",
                Dock = DockStyle.Top,
                Height = 30,
                ForeColor = MetadataPaletteControl.LabelColor,
                BackColor = MetadataPaletteControl.BgColor,
                Font = new DrawingFont("Segoe UI", 9),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0)
            };

            // Header
            header = new SectionHeader("MC Part Properties");

            // Stack in reverse for Dock=Top
            this.Controls.Add(propertiesContent);
            this.Controls.Add(messageLabel);
            this.Controls.Add(header);
        }

        public void ShowMessage(string message)
        {
            messageLabel.Text = message;
            messageLabel.Visible = true;
            propertiesContent.Visible = false;
        }

        public void ShowProperties(string partName, string handle, string layer,
            double width, double depth, double height)
        {
            messageLabel.Visible = false;
            propertiesContent.Visible = true;

            handleRow.SetValue(handle);
            layerRow.SetValue(layer);
            widthRow.SetValue($"{width:F2}");
            depthRow.SetValue($"{depth:F2}");
            heightRow.SetValue($"{height:F2}");
            partNameRow.SetValue(partName);
        }

        public string GetPartName() => partNameRow.GetValue();
    }
}
