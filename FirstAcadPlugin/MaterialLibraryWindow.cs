using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Material library editor. Shows all material_plates for the open project
    /// as a flat grid: Material | Code | Thickness | Width | Height | Density.
    /// Supports add/edit/delete via toolbar buttons.
    /// </summary>
    public class MaterialLibraryWindow : Window
    {
        private DataGrid _grid;

        public MaterialLibraryWindow()
        {
            Title = DatabaseService.CurrentProject != null
                ? $"Material Library — {DatabaseService.CurrentProject.Name}"
                : "Material Library";
            Width  = 680;
            Height = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Background = DarkBrush(45, 45, 48);
            Content = BuildLayout();
            Loaded += (s, e) => Refresh();
        }

        // ─── Layout ───────────────────────────────────────────────────────────

        private UIElement BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 8, 8, 4) };
            toolbar.Children.Add(ToolBtn("Add Material",    OnAddMaterial));
            toolbar.Children.Add(ToolBtn("Add Plate",       OnAddPlate));
            toolbar.Children.Add(ToolBtn("Edit Selected",   OnEditSelected));
            toolbar.Children.Add(ToolBtn("Delete Selected", OnDeleteSelected));
            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            _grid = new DataGrid
            {
                Margin = new Thickness(8, 4, 8, 4),
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
            };

            Col("Material",        "MaterialName",     new DataGridLength(1, DataGridLengthUnitType.Star));
            Col("Code",            "Code",             70);
            Col("Thickness (mm)",  "ThicknessDisplay", 115);
            Col("Width (mm)",      "WidthDisplay",     90);
            Col("Height (mm)",     "HeightDisplay",    90);
            Col("Density (kg/m³)", "DensityDisplay",   115);

            Grid.SetRow(_grid, 1);
            root.Children.Add(_grid);

            var closeBtn = new Button
            {
                Content = "Close", Width = 90, Height = 28,
                Margin = new Thickness(0, 4, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = DarkBrush(60, 60, 65), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), IsCancel = true,
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetRow(closeBtn, 2);
            root.Children.Add(closeBtn);

            return root;
        }

        private void Col(string header, string binding, DataGridLength width)
        {
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width = width,
            });
        }
        private void Col(string header, string binding, double pixelWidth)
            => Col(header, binding, new DataGridLength(pixelWidth));

        private static Button ToolBtn(string text, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = text, Height = 28,
                Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 0, 10, 0),
                Background = DarkBrush(70, 70, 75), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 12,
            };
            b.Click += click;
            return b;
        }

        // ─── Data ─────────────────────────────────────────────────────────────

        private void Refresh()
        {
            try
            {
                var plates = MaterialLibraryService.GetPlatesForCurrentProject();
                var rows = new List<PlateViewModel>();
                foreach (var p in plates) rows.Add(new PlateViewModel(p));
                _grid.ItemsSource = rows;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error loading materials:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private PlateViewModel Selected => _grid.SelectedItem as PlateViewModel;

        // ─── Toolbar actions ──────────────────────────────────────────────────

        private void OnAddMaterial(object sender, RoutedEventArgs e)
        {
            var dlg = new AddEditMaterialDialog();
            if (dlg.ShowDialog() != true) return;
            Try(() => MaterialLibraryService.SaveMaterial(new Material
            {
                Id = Guid.Empty,
                Name = dlg.MaterialName,
                Density = dlg.Density,
                Description = dlg.Description,
            }));
        }

        private void OnAddPlate(object sender, RoutedEventArgs e)
        {
            var mats = MaterialLibraryService.GetMaterials();
            if (mats.Count == 0)
            {
                MessageBox.Show("Add at least one material first.", "No Materials",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new AddEditPlateDialog(mats);
            if (dlg.ShowDialog() != true) return;
            Try(() => MaterialLibraryService.SavePlate(new MaterialPlate
            {
                Id = Guid.Empty,
                MaterialId = dlg.SelectedMaterialId,
                Code = dlg.Code,
                Width = dlg.PlateWidth, Height = dlg.PlateHeight,
                Thickness = dlg.Thickness, IsDefault = dlg.IsDefault,
            }));
        }

        private void OnEditSelected(object sender, RoutedEventArgs e)
        {
            var row = Selected;
            if (row == null) { NoSelectionMsg(); return; }
            var mats = MaterialLibraryService.GetMaterials();
            var dlg = new AddEditPlateDialog(mats, row.Plate);
            if (dlg.ShowDialog() != true) return;
            Try(() => MaterialLibraryService.SavePlate(new MaterialPlate
            {
                Id = row.PlateId,
                MaterialId = dlg.SelectedMaterialId,
                Code = dlg.Code,
                Width = dlg.PlateWidth, Height = dlg.PlateHeight,
                Thickness = dlg.Thickness, IsDefault = dlg.IsDefault,
            }));
        }

        private void OnDeleteSelected(object sender, RoutedEventArgs e)
        {
            var row = Selected;
            if (row == null) { NoSelectionMsg(); return; }
            if (MessageBox.Show(
                    $"Delete plate '{row.Code}' for material '{row.MaterialName}'?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;
            Try(() => MaterialLibraryService.DeletePlate(row.PlateId));
        }

        private void Try(Action action)
        {
            try { action(); Refresh(); }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void NoSelectionMsg() =>
            MessageBox.Show("Select a row first.", "Nothing Selected",
                MessageBoxButton.OK, MessageBoxImage.Information);

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

        // ─── ViewModel ────────────────────────────────────────────────────────

        public class PlateViewModel
        {
            public readonly MaterialPlate Plate;
            public Guid   PlateId          => Plate.Id;
            public string MaterialName     => Plate.MaterialName ?? "—";
            public string Code             => Plate.Code;
            public string ThicknessDisplay => $"{Plate.Thickness:0.##}";
            public string WidthDisplay     => $"{Plate.Width:0.##}";
            public string HeightDisplay    => $"{Plate.Height:0.##}";
            public string DensityDisplay   => $"{Plate.MaterialDensity:0}";
            public PlateViewModel(MaterialPlate plate) { Plate = plate; }
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
