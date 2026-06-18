using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace MCConstructor
{
    /// <summary>
    /// Dialog for selecting a sheet material and basic fill options.
    /// Uses material_plates as 2D sheet sizes.
    /// </summary>
    public class MaterialFillDialog : Window
    {
        public MaterialPlate SelectedPlate { get; private set; }
        public double Gap { get; private set; }
        public bool AllowRotate { get; private set; } = true;

        private ComboBox _materialFilter;
        private DataGrid _platesGrid;
        private TextBox _gapBox;
        private CheckBox _rotateCheck;
        private List<MaterialPlate> _allPlates = new List<MaterialPlate>();

        public MaterialFillDialog()
        {
            Title = "Fill Boundary With Material";
            Width = 540;
            Height = 500;
            MinWidth = 500;
            MinHeight = 430;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Background = Brush(45, 45, 48);
            DialogTheme.Apply(this);
            Content = Build();
            Loaded += OnLoaded;
        }

        private UIElement Build()
        {
            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(Row(GridUnitType.Auto));
            root.RowDefinitions.Add(Row(GridUnitType.Auto));
            root.RowDefinitions.Add(Row(GridUnitType.Star));
            root.RowDefinitions.Add(Row(GridUnitType.Auto));
            root.RowDefinitions.Add(Row(GridUnitType.Auto));

            var header = new TextBlock
            {
                Text = "Sheet Material",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Set(root, header, 0);

            var filterRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            filterRow.Children.Add(new TextBlock
            {
                Text = "Material:",
                Foreground = Brush(200, 200, 200),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            _materialFilter = new ComboBox
            {
                Width = 220,
                Height = 26,
                Background = Brush(37, 37, 38),
                Foreground = Brushes.White,
                BorderBrush = Brush(67, 67, 70)
            };
            _materialFilter.SelectionChanged += OnMaterialFilterChanged;
            filterRow.Children.Add(_materialFilter);
            Set(root, filterRow, 1);

            _platesGrid = BuildPlatesGrid();
            Set(root, _platesGrid, 2);

            var options = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            options.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            options.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            options.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var gapLabel = new TextBlock
            {
                Text = "Joint gap (mm):",
                Foreground = Brush(200, 200, 200),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 8)
            };
            Grid.SetRow(gapLabel, 0);
            Grid.SetColumn(gapLabel, 0);
            options.Children.Add(gapLabel);

            _gapBox = new TextBox
            {
                Text = "0",
                Height = 26,
                Padding = new Thickness(6, 3, 6, 3),
                Background = Brush(37, 37, 38),
                Foreground = Brushes.White,
                BorderBrush = Brush(67, 67, 70),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(_gapBox, 0);
            Grid.SetColumn(_gapBox, 1);
            options.Children.Add(_gapBox);

            _rotateCheck = new CheckBox
            {
                Content = "Choose sheet direction automatically",
                IsChecked = true,
                Foreground = Brush(200, 200, 200),
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(_rotateCheck, 1);
            Grid.SetColumnSpan(_rotateCheck, 3);
            options.Children.Add(_rotateCheck);

            Set(root, options, 3);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            btnRow.Children.Add(DlgBtn("Cancel", false, Brush(70, 70, 75), (s, e) => { DialogResult = false; Close(); }));
            btnRow.Children.Add(DlgBtn("Fill Boundary", true, Brush(0, 122, 204), OnOk));
            Set(root, btnRow, 4);

            return root;
        }

        private DataGrid BuildPlatesGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                Background = Brush(37, 37, 38),
                Foreground = Brushes.White,
                RowBackground = Brush(45, 45, 48),
                AlternatingRowBackground = Brush(50, 50, 53),
                BorderBrush = Brush(67, 67, 70),
                BorderThickness = new Thickness(1),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = Brush(67, 67, 70),
                RowHeaderWidth = 0
            };

            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brush(55, 55, 60)));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brush(200, 200, 200)));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(75, 75, 80)));
            headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            headerStyle.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(TextElement.FontSizeProperty, 11.0));
            grid.ColumnHeaderStyle = headerStyle;

            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            var selCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selCell.Setters.Add(new Setter(Control.BackgroundProperty, Brush(0, 102, 180)));
            selCell.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            cellStyle.Triggers.Add(selCell);
            grid.CellStyle = cellStyle;

            grid.Columns.Add(Col("Code", "Code", 90));
            grid.Columns.Add(Col("Material", "MaterialName", new DataGridLength(1, DataGridLengthUnitType.Star)));
            grid.Columns.Add(Col("Sheet Size", "DimensionsText", 150));
            grid.Columns.Add(Col("Thickness", "ThicknessText", 85));

            grid.MouseDoubleClick += (s, e) => { if (_platesGrid.SelectedItem != null) OnOk(s, e); };
            return grid;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try { _allPlates = MaterialLibraryService.GetPlatesForCurrentProject(); }
            catch { _allPlates = new List<MaterialPlate>(); }

            _materialFilter.Items.Clear();
            _materialFilter.Items.Add(new ComboBoxItem { Content = "All Materials", Tag = null });

            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var plate in _allPlates)
            {
                string name = plate.MaterialName ?? "";
                if (seen.Add(name))
                    _materialFilter.Items.Add(new ComboBoxItem { Content = name, Tag = name });
            }

            _materialFilter.SelectedIndex = 0;
            ApplyMaterialFilter(null);

            foreach (PlateRow row in _platesGrid.Items)
            {
                if (row.Plate.IsDefault) { _platesGrid.SelectedItem = row; break; }
            }
            if (_platesGrid.SelectedItem == null && _platesGrid.Items.Count > 0)
                _platesGrid.SelectedIndex = 0;
        }

        private void OnMaterialFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = _materialFilter.SelectedItem as ComboBoxItem;
            ApplyMaterialFilter(item?.Tag as string);
        }

        private void ApplyMaterialFilter(string materialName)
        {
            var rows = new List<PlateRow>();
            foreach (var plate in _allPlates)
            {
                if (materialName == null || plate.MaterialName == materialName)
                    rows.Add(new PlateRow(plate));
            }
            _platesGrid.ItemsSource = rows;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var row = _platesGrid.SelectedItem as PlateRow;
            if (row == null)
            {
                Warn("Select a sheet size from the material library.");
                return;
            }

            if (!double.TryParse(_gapBox.Text, out double gap) || gap < 0)
            {
                Warn("Enter a valid joint gap (0 or greater).");
                _gapBox.Focus();
                return;
            }

            SelectedPlate = row.Plate;
            Gap = gap;
            AllowRotate = _rotateCheck.IsChecked == true;
            DialogResult = true;
            Close();
        }

        private static RowDefinition Row(GridUnitType t, double value = 1)
            => new RowDefinition { Height = new GridLength(value, t) };

        private static void Set(Grid grid, UIElement element, int row)
        {
            Grid.SetRow(element, row);
            grid.Children.Add(element);
        }

        private static DataGridTextColumn Col(string header, string binding, DataGridLength width)
            => new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width = width
            };

        private static DataGridTextColumn Col(string header, string binding, double px)
            => Col(header, binding, new DataGridLength(px));

        private static Button DlgBtn(string text, bool isDefault, SolidColorBrush bg, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = text,
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(6, 0, 0, 0),
                Background = bg,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = isDefault ? FontWeights.SemiBold : FontWeights.Normal,
                IsDefault = isDefault,
                IsCancel = !isDefault
            };
            b.Click += click;
            return b;
        }

        private void Warn(string msg) =>
            MessageBox.Show(msg, "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);

        private static SolidColorBrush Brush(byte r, byte g, byte b)
            => new SolidColorBrush(Color.FromRgb(r, g, b));

        private class PlateRow
        {
            public readonly MaterialPlate Plate;
            public string Code => Plate.Code;
            public string MaterialName => Plate.MaterialName ?? "";
            public string DimensionsText => $"{Plate.Width:0.##} x {Plate.Height:0.##} mm";
            public string ThicknessText => $"{Plate.Thickness:0.##} mm";
            public PlateRow(MaterialPlate plate) { Plate = plate; }
        }
    }
}
