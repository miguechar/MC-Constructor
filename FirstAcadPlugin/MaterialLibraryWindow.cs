using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Material library editor.
    /// Top section: every material for the project (Name, Density, Description).
    /// Bottom section: plate sizes for the selected material (Code, Thickness, Width, Height).
    /// Selecting a material refreshes the plates grid automatically.
    /// </summary>
    public class MaterialLibraryWindow : Window
    {
        private DataGrid _materialsGrid;
        private DataGrid _platesGrid;
        private TextBlock _platesHeader;

        // Per-section action buttons — enabled/disabled based on selection state.
        private Button _editMatBtn;
        private Button _deleteMatBtn;
        private Button _addPlateBtn;
        private Button _editPlateBtn;
        private Button _deletePlateBtn;

        public MaterialLibraryWindow()
        {
            Title = DatabaseService.CurrentProject != null
                ? $"Material Library — {DatabaseService.CurrentProject.Name}"
                : "Material Library";
            Width  = 760;
            Height = 580;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Background = DarkBrush(45, 45, 48);
            Content = BuildLayout();
            Loaded += (s, e) => RefreshMaterials();
        }

        // ─── Layout ───────────────────────────────────────────────────────────

        private UIElement BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });          // splitter
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });             // close

            // ── Materials section ────────────────────────────────────────────
            var matSection = new Grid();
            matSection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            matSection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // toolbar
            matSection.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // grid

            var matLabel = SectionLabel("MATERIALS");
            Grid.SetRow(matLabel, 0);
            matSection.Children.Add(matLabel);

            var matToolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 4) };
            matToolbar.Children.Add(ToolBtn("Add Material", OnAddMaterial));
            _editMatBtn   = ToolBtn("Edit",   OnEditMaterial);   _editMatBtn.IsEnabled   = false;
            _deleteMatBtn = ToolBtn("Delete", OnDeleteMaterial); _deleteMatBtn.IsEnabled = false;
            matToolbar.Children.Add(_editMatBtn);
            matToolbar.Children.Add(_deleteMatBtn);
            Grid.SetRow(matToolbar, 1);
            matSection.Children.Add(matToolbar);

            _materialsGrid = MakeGrid();
            AddCol(_materialsGrid, "Name",             "Name",        new DataGridLength(1, DataGridLengthUnitType.Star));
            AddCol(_materialsGrid, "Density (kg/m³)",  "DensityText", 120);
            AddCol(_materialsGrid, "Description",      "Description", new DataGridLength(1, DataGridLengthUnitType.Star));
            _materialsGrid.SelectionChanged += OnMaterialSelectionChanged;
            Grid.SetRow(_materialsGrid, 2);
            matSection.Children.Add(_materialsGrid);

            Grid.SetRow(matSection, 0);
            root.Children.Add(matSection);

            // ── Splitter ────────────────────────────────────────────────────
            var splitter = new GridSplitter
            {
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment   = VerticalAlignment.Stretch,
                Background = DarkBrush(62, 62, 66),
            };
            Grid.SetRow(splitter, 1);
            root.Children.Add(splitter);

            // ── Plates section ──────────────────────────────────────────────
            var plateSection = new Grid();
            plateSection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            plateSection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // toolbar
            plateSection.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // grid

            _platesHeader = SectionLabel("PLATES");
            Grid.SetRow(_platesHeader, 0);
            plateSection.Children.Add(_platesHeader);

            var plateToolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 4) };
            _addPlateBtn    = ToolBtn("Add Plate", OnAddPlate);      _addPlateBtn.IsEnabled    = false;
            _editPlateBtn   = ToolBtn("Edit",      OnEditPlate);     _editPlateBtn.IsEnabled   = false;
            _deletePlateBtn = ToolBtn("Delete",    OnDeletePlate);   _deletePlateBtn.IsEnabled = false;
            plateToolbar.Children.Add(_addPlateBtn);
            plateToolbar.Children.Add(_editPlateBtn);
            plateToolbar.Children.Add(_deletePlateBtn);
            Grid.SetRow(plateToolbar, 1);
            plateSection.Children.Add(plateToolbar);

            _platesGrid = MakeGrid();
            AddCol(_platesGrid, "Code",           "Code",             90);
            AddCol(_platesGrid, "Thickness (mm)",  "ThicknessDisplay", 115);
            AddCol(_platesGrid, "Width (mm)",      "WidthDisplay",     100);
            AddCol(_platesGrid, "Height (mm)",     "HeightDisplay",    100);
            AddCol(_platesGrid, "Default",         "DefaultDisplay",   70);
            _platesGrid.SelectionChanged += OnPlateSelectionChanged;
            Grid.SetRow(_platesGrid, 2);
            plateSection.Children.Add(_platesGrid);

            Grid.SetRow(plateSection, 2);
            root.Children.Add(plateSection);

            // ── Close button ─────────────────────────────────────────────────
            var closeBtn = new Button
            {
                Content = "Close", Width = 90, Height = 28,
                Margin = new Thickness(0, 6, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = DarkBrush(60, 60, 65), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), IsCancel = true,
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetRow(closeBtn, 3);
            root.Children.Add(closeBtn);

            return root;
        }

        // Build a styled DataGrid with dark header and dark rows.
        private static DataGrid MakeGrid()
        {
            var grid = new DataGrid
            {
                Margin = new Thickness(8, 0, 8, 4),
                AutoGenerateColumns = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                Background = DarkBrush(37, 37, 38),
                Foreground = Brushes.White,
                RowBackground = DarkBrush(45, 45, 48),
                AlternatingRowBackground = DarkBrush(50, 50, 53),
                BorderBrush = DarkBrush(67, 67, 70),
                BorderThickness = new Thickness(1),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = DarkBrush(67, 67, 70),
                RowHeaderWidth = 0,
            };

            // Dark column-header style — must be set explicitly; WPF DataGrid
            // ignores Background/Foreground on the grid itself for headers.
            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty,      DarkBrush(55, 55, 60)));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty,      new SolidColorBrush(Color.FromRgb(200, 200, 200))));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty,     DarkBrush(75, 75, 80)));
            headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty,          new Thickness(8, 5, 8, 5)));
            headerStyle.Setters.Add(new Setter(TextElement.FontWeightProperty,  FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(TextElement.FontSizeProperty,    11.0));
            grid.ColumnHeaderStyle = headerStyle;

            // Dark cell style so selected rows stay legible.
            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            var selectedTrigger = new Trigger
            {
                Property = DataGridCell.IsSelectedProperty,
                Value    = true,
            };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, DarkBrush(0, 102, 180)));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            cellStyle.Triggers.Add(selectedTrigger);
            grid.CellStyle = cellStyle;

            // Row style — keep the dark alternating backgrounds when selected.
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            var rowSelectedTrigger = new Trigger
            {
                Property = DataGridRow.IsSelectedProperty,
                Value    = true,
            };
            rowSelectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, DarkBrush(0, 102, 180)));
            rowSelectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            rowStyle.Triggers.Add(rowSelectedTrigger);
            grid.RowStyle = rowStyle;

            return grid;
        }

        private static void AddCol(DataGrid grid, string header, string binding, DataGridLength width)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header  = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width   = width,
            });
        }
        private static void AddCol(DataGrid grid, string header, string binding, double px)
            => AddCol(grid, header, binding, new DataGridLength(px));

        private static TextBlock SectionLabel(string text) => new TextBlock
        {
            Text       = text,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 165)),
            FontSize   = 10,
            FontWeight = FontWeights.Bold,
            Margin     = new Thickness(10, 8, 8, 2),
        };

        private static Button ToolBtn(string text, RoutedEventHandler click)
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty,      DarkBrush(70, 70, 75)));
            style.Setters.Add(new Setter(Control.ForegroundProperty,      Brushes.White));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

            // Disabled state: dim the button to match the dark theme instead
            // of letting WPF paint it white/grey.
            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(Control.BackgroundProperty, DarkBrush(52, 52, 55)));
            disabledTrigger.Setters.Add(new Setter(Control.ForegroundProperty, DarkBrush(100, 100, 105)));
            style.Triggers.Add(disabledTrigger);

            var b = new Button
            {
                Content = text, Height = 28,
                Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 0, 12, 0),
                FontSize = 12,
                Style = style,
            };
            b.Click += click;
            return b;
        }

        // ─── Data ─────────────────────────────────────────────────────────────

        private void RefreshMaterials()
        {
            try
            {
                var mats = MaterialLibraryService.GetMaterials();
                var rows = new List<MaterialViewModel>();
                foreach (var m in mats) rows.Add(new MaterialViewModel(m));
                _materialsGrid.ItemsSource = rows;

                _editMatBtn.IsEnabled   = false;
                _deleteMatBtn.IsEnabled = false;
                _addPlateBtn.IsEnabled  = false;
                RefreshPlates(null);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error loading materials:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshPlates(Material mat)
        {
            if (mat == null)
            {
                _platesHeader.Text = "PLATES  —  select a material above";
                _platesGrid.ItemsSource = null;
                _addPlateBtn.IsEnabled  = false;
                _editPlateBtn.IsEnabled = false;
                _deletePlateBtn.IsEnabled = false;
                return;
            }

            _platesHeader.Text = $"PLATES  —  {mat.Name}";
            _addPlateBtn.IsEnabled = true;

            try
            {
                var plates = MaterialLibraryService.GetPlates(mat.Id);
                var rows = new List<PlateViewModel>();
                foreach (var p in plates) rows.Add(new PlateViewModel(p));
                _platesGrid.ItemsSource = rows;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error loading plates:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private MaterialViewModel SelectedMaterialRow => _materialsGrid.SelectedItem as MaterialViewModel;
        private PlateViewModel    SelectedPlateRow    => _platesGrid.SelectedItem    as PlateViewModel;

        // ─── Selection handlers ───────────────────────────────────────────────

        private void OnMaterialSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasMat = SelectedMaterialRow != null;
            _editMatBtn.IsEnabled   = hasMat;
            _deleteMatBtn.IsEnabled = hasMat;
            _editPlateBtn.IsEnabled   = false;
            _deletePlateBtn.IsEnabled = false;
            RefreshPlates(hasMat ? SelectedMaterialRow.Material : null);
        }

        private void OnPlateSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasPlate = SelectedPlateRow != null;
            _editPlateBtn.IsEnabled   = hasPlate;
            _deletePlateBtn.IsEnabled = hasPlate;
        }

        // ─── Toolbar actions ──────────────────────────────────────────────────

        private void OnAddMaterial(object sender, RoutedEventArgs e)
        {
            var dlg = new AddEditMaterialDialog();
            if (dlg.ShowDialog() != true) return;
            Try(() => MaterialLibraryService.SaveMaterial(new Material
            {
                Id          = Guid.Empty,
                Name        = dlg.MaterialName,
                Density     = dlg.Density,
                Description = dlg.Description,
            }), refreshMaterials: true);
        }

        private void OnEditMaterial(object sender, RoutedEventArgs e)
        {
            var row = SelectedMaterialRow;
            if (row == null) return;
            var dlg = new AddEditMaterialDialog(row.Material);
            if (dlg.ShowDialog() != true) return;
            Try(() => MaterialLibraryService.SaveMaterial(new Material
            {
                Id          = row.Material.Id,
                Name        = dlg.MaterialName,
                Density     = dlg.Density,
                Description = dlg.Description,
            }), refreshMaterials: true);
        }

        private void OnDeleteMaterial(object sender, RoutedEventArgs e)
        {
            var row = SelectedMaterialRow;
            if (row == null) return;
            if (MessageBox.Show(
                    $"Delete material '{row.Name}' and ALL its plates?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;
            Try(() => MaterialLibraryService.DeleteMaterial(row.Material.Id), refreshMaterials: true);
        }

        private void OnAddPlate(object sender, RoutedEventArgs e)
        {
            var mat = SelectedMaterialRow?.Material;
            if (mat == null) return;
            var dlg = new AddEditPlateDialog(new List<Material> { mat });
            if (dlg.ShowDialog() != true) return;
            Try(() => MaterialLibraryService.SavePlate(new MaterialPlate
            {
                Id = Guid.Empty,
                MaterialId = mat.Id,
                Code = dlg.Code,
                Width = dlg.PlateWidth, Height = dlg.PlateHeight,
                Thickness = dlg.Thickness, IsDefault = dlg.IsDefault,
            }), refreshMaterials: false);
        }

        private void OnEditPlate(object sender, RoutedEventArgs e)
        {
            var row = SelectedPlateRow;
            var mat = SelectedMaterialRow?.Material;
            if (row == null || mat == null) return;
            var dlg = new AddEditPlateDialog(new List<Material> { mat }, row.Plate);
            if (dlg.ShowDialog() != true) return;
            Try(() => MaterialLibraryService.SavePlate(new MaterialPlate
            {
                Id         = row.PlateId,
                MaterialId = mat.Id,
                Code = dlg.Code,
                Width = dlg.PlateWidth, Height = dlg.PlateHeight,
                Thickness = dlg.Thickness, IsDefault = dlg.IsDefault,
            }), refreshMaterials: false);
        }

        private void OnDeletePlate(object sender, RoutedEventArgs e)
        {
            var row = SelectedPlateRow;
            if (row == null) return;
            if (MessageBox.Show(
                    $"Delete plate '{row.Code}'?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;
            Try(() => MaterialLibraryService.DeletePlate(row.PlateId), refreshMaterials: false);
        }

        private void Try(Action action, bool refreshMaterials)
        {
            try
            {
                action();
                var previousMat = SelectedMaterialRow?.Material;
                if (refreshMaterials)
                    RefreshMaterials();
                else
                    RefreshPlates(previousMat);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── Shared helpers ───────────────────────────────────────────────────

        internal static SolidColorBrush DarkBrush(byte r, byte g, byte b)
            => new SolidColorBrush(Color.FromRgb(r, g, b));

        internal static TextBox MakeBox(string text) => new TextBox
        {
            Text = text, Margin = new Thickness(0, 4, 0, 4),
            Padding = new Thickness(6, 3, 6, 3),
            Background = DarkBrush(37, 37, 38), Foreground = Brushes.White,
            BorderBrush = DarkBrush(67, 67, 70), BorderThickness = new Thickness(1),
        };

        internal static void FormRow(Grid g, int row, string labelText, UIElement ctrl)
        {
            var lbl = new Label
            {
                Content = labelText, Margin = new Thickness(0, 4, 8, 4),
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0); g.Children.Add(lbl);
            Grid.SetRow(ctrl, row); Grid.SetColumn(ctrl, 1); g.Children.Add(ctrl);
        }

        internal static Button DialogBtn(string text, bool isDefault, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = text, Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0),
                Background = isDefault ? DarkBrush(0, 122, 204) : DarkBrush(60, 60, 65),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                IsDefault = isDefault, IsCancel = !isDefault,
            };
            b.Click += click;
            return b;
        }

        // ─── ViewModels ───────────────────────────────────────────────────────

        public class MaterialViewModel
        {
            public readonly Material Material;
            public string Name        => Material.Name;
            public string DensityText => $"{Material.Density:0} kg/m³";
            public string Description => Material.Description ?? "";
            public MaterialViewModel(Material m) { Material = m; }
        }

        public class PlateViewModel
        {
            public readonly MaterialPlate Plate;
            public Guid   PlateId          => Plate.Id;
            public string Code             => Plate.Code;
            public string ThicknessDisplay => $"{Plate.Thickness:0.##}";
            public string WidthDisplay     => $"{Plate.Width:0.##}";
            public string HeightDisplay    => $"{Plate.Height:0.##}";
            public string DefaultDisplay   => Plate.IsDefault ? "✓" : "";
            public PlateViewModel(MaterialPlate p) { Plate = p; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Add / edit a material grade
    // ─────────────────────────────────────────────────────────────────────────
    public class AddEditMaterialDialog : Window
    {
        private readonly TextBox _nameBox;
        private readonly TextBox _densityBox;
        private readonly TextBox _descBox;

        public string MaterialName { get; private set; }
        public double Density      { get; private set; }
        public string Description  { get; private set; }

        public AddEditMaterialDialog(Material existing = null)
        {
            Title  = existing == null ? "Add Material" : "Edit Material";
            Width  = 360; Height = 230;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = MaterialLibraryWindow.DarkBrush(45, 45, 48);

            var g = new Grid { Margin = new Thickness(16) };
            for (int i = 0; i < 4; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            MaterialLibraryWindow.FormRow(g, 0, "Name:",            _nameBox    = MaterialLibraryWindow.MakeBox(existing?.Name ?? ""));
            MaterialLibraryWindow.FormRow(g, 1, "Density (kg/m³):", _densityBox = MaterialLibraryWindow.MakeBox(existing?.Density.ToString("0") ?? "7850"));
            MaterialLibraryWindow.FormRow(g, 2, "Description:",     _descBox    = MaterialLibraryWindow.MakeBox(existing?.Description ?? ""));

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Grid.SetRow(btns, 3); Grid.SetColumnSpan(btns, 2);
            btns.Children.Add(MaterialLibraryWindow.DialogBtn("OK",     true,  OnOk));
            btns.Children.Add(MaterialLibraryWindow.DialogBtn("Cancel", false, (s, e) => { DialogResult = false; Close(); }));
            g.Children.Add(btns);

            Content = g;
            Loaded += (s, e) => _nameBox.Focus();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            { Warn("Material name is required."); _nameBox.Focus(); return; }
            if (!double.TryParse(_densityBox.Text, out double d) || d <= 0)
            { Warn("Enter a positive density (kg/m³)."); _densityBox.Focus(); return; }
            MaterialName = _nameBox.Text.Trim();
            Density      = d;
            Description  = _descBox.Text.Trim();
            DialogResult = true; Close();
        }

        private void Warn(string msg) =>
            MessageBox.Show(msg, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Add / edit a plate (size entry for a material)
    // ─────────────────────────────────────────────────────────────────────────
    public class AddEditPlateDialog : Window
    {
        private readonly ComboBox _matCombo;
        private readonly TextBox _codeBox;
        private readonly TextBox _thicknessBox;
        private readonly TextBox _widthBox;
        private readonly TextBox _heightBox;
        private readonly CheckBox _defaultChk;

        public Guid   SelectedMaterialId { get; private set; }
        public string Code        { get; private set; }
        public double Thickness   { get; private set; }
        public double PlateWidth  { get; private set; }
        public double PlateHeight { get; private set; }
        public bool   IsDefault   { get; private set; }

        public AddEditPlateDialog(List<Material> materials, MaterialPlate existing = null)
        {
            Title  = existing == null ? "Add Plate" : "Edit Plate";
            Width  = 360; Height = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = MaterialLibraryWindow.DarkBrush(45, 45, 48);

            var g = new Grid { Margin = new Thickness(16) };
            for (int i = 0; i < 7; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _matCombo = new ComboBox
            {
                Margin = new Thickness(0, 4, 0, 4),
                Background = MaterialLibraryWindow.DarkBrush(37, 37, 38),
                Foreground = Brushes.White,
                BorderBrush = MaterialLibraryWindow.DarkBrush(67, 67, 70),
            };
            foreach (var m in materials)
                _matCombo.Items.Add(new ComboBoxItem { Content = m.Name, Tag = m.Id });
            if (existing != null)
            {
                for (int i = 0; i < _matCombo.Items.Count; i++)
                    if (((ComboBoxItem)_matCombo.Items[i]).Tag is Guid gid && gid == existing.MaterialId)
                    { _matCombo.SelectedIndex = i; break; }
            }
            else if (_matCombo.Items.Count > 0)
                _matCombo.SelectedIndex = 0;

            MaterialLibraryWindow.FormRow(g, 0, "Material:",          _matCombo);
            MaterialLibraryWindow.FormRow(g, 1, "Code (e.g. T10):",   _codeBox      = MaterialLibraryWindow.MakeBox(existing?.Code ?? ""));
            MaterialLibraryWindow.FormRow(g, 2, "Thickness (mm):",    _thicknessBox = MaterialLibraryWindow.MakeBox(existing?.Thickness.ToString("0.##") ?? ""));
            MaterialLibraryWindow.FormRow(g, 3, "Width (mm):",        _widthBox     = MaterialLibraryWindow.MakeBox(existing?.Width.ToString("0.##") ?? ""));
            MaterialLibraryWindow.FormRow(g, 4, "Height (mm):",       _heightBox    = MaterialLibraryWindow.MakeBox(existing?.Height.ToString("0.##") ?? ""));

            _defaultChk = new CheckBox
            {
                Content = "Set as default plate for this material",
                IsChecked = existing?.IsDefault ?? false,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Margin = new Thickness(0, 6, 0, 4),
            };
            Grid.SetRow(_defaultChk, 5); Grid.SetColumnSpan(_defaultChk, 2);
            g.Children.Add(_defaultChk);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(btns, 6); Grid.SetColumnSpan(btns, 2);
            btns.Children.Add(MaterialLibraryWindow.DialogBtn("OK",     true,  OnOk));
            btns.Children.Add(MaterialLibraryWindow.DialogBtn("Cancel", false, (s, e) => { DialogResult = false; Close(); }));
            g.Children.Add(btns);

            Content = g;
            Loaded += (s, e) => _codeBox.Focus();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (_matCombo.SelectedItem == null)           { Warn("Select a material."); return; }
            if (string.IsNullOrWhiteSpace(_codeBox.Text)) { Warn("Code is required (e.g. T10)."); _codeBox.Focus(); return; }
            if (!double.TryParse(_thicknessBox.Text, out double t) || t <= 0) { Warn("Enter a positive thickness (mm)."); _thicknessBox.Focus(); return; }
            if (!double.TryParse(_widthBox.Text,     out double w) || w <= 0) { Warn("Enter a positive width (mm).");     _widthBox.Focus();     return; }
            if (!double.TryParse(_heightBox.Text,    out double h) || h <= 0) { Warn("Enter a positive height (mm).");    _heightBox.Focus();    return; }

            SelectedMaterialId = (Guid)((ComboBoxItem)_matCombo.SelectedItem).Tag;
            Code        = _codeBox.Text.Trim();
            Thickness   = t; PlateWidth = w; PlateHeight = h;
            IsDefault   = _defaultChk.IsChecked == true;
            DialogResult = true; Close();
        }

        private void Warn(string msg) =>
            MessageBox.Show(msg, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
