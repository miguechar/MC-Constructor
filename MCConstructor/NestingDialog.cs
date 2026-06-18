using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace MCConstructor
{
    /// <summary>
    /// Dialog for configuring a nesting run.
    /// The user can pick a plate from the project's library
    /// or enter plate dimensions manually.
    /// </summary>
    public class NestingDialog : Window
    {
        // ── Outputs ────────────────────────────────────────────────────────────
        public double PlateWidth     { get; private set; }
        public double PlateHeight    { get; private set; }
        public double PlateThickness { get; private set; }
        public double Spacing        { get; private set; } = 10.0;
        /// <summary>Non-null when the user picked a plate from the library.</summary>
        public MaterialPlate SelectedPlate { get; private set; }

        // ── State ──────────────────────────────────────────────────────────────
        private bool _useLibrary = true;

        // ── Controls ───────────────────────────────────────────────────────────
        private RadioButton _radioLibrary;
        private RadioButton _radioManual;
        private Panel       _libraryPanel;
        private Panel       _manualPanel;
        private ComboBox    _materialFilter;
        private DataGrid    _platesGrid;
        private TextBox     _widthBox;
        private TextBox     _heightBox;
        private TextBox     _spacingBox;

        // ── Raw data ───────────────────────────────────────────────────────────
        private List<MaterialPlate> _allPlates = new List<MaterialPlate>();

        public NestingDialog(int selectedPartsCount)
        {
            Title  = "Create Nesting Layout";
            Width  = 520;
            Height = 560;
            MinWidth  = 480;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Background = Brush(45, 45, 48);
            Content = Build(selectedPartsCount);
            Loaded += OnLoaded;
        }

        // ── Build UI ──────────────────────────────────────────────────────────

        private UIElement Build(int partCount)
        {
            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(Row(GridUnitType.Auto));   // 0 header
            root.RowDefinitions.Add(Row(GridUnitType.Auto));   // 1 parts info
            root.RowDefinitions.Add(Row(GridUnitType.Auto));   // 2 radio row
            root.RowDefinitions.Add(Row(GridUnitType.Star));   // 3 library/manual panels
            root.RowDefinitions.Add(Row(GridUnitType.Auto));   // 4 spacing
            root.RowDefinitions.Add(Row(GridUnitType.Auto));   // 5 buttons

            // Header
            var header = new TextBlock
            {
                Text = "Plate Selection",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10),
            };
            Set(root, header, 0);

            // Parts count
            var partsInfo = new TextBlock
            {
                Text = $"Selected parts: {partCount}",
                Foreground = Brush(0, 200, 100),
                Margin = new Thickness(0, 0, 0, 14),
            };
            Set(root, partsInfo, 1);

            // Radio buttons
            var radioRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10),
            };
            _radioLibrary = Radio("Pick from plate library", true);
            _radioManual  = Radio("Enter manually",          false);
            _radioLibrary.Checked += (s, e) => SwitchMode(library: true);
            _radioManual.Checked  += (s, e) => SwitchMode(library: false);
            radioRow.Children.Add(_radioLibrary);
            radioRow.Children.Add(new TextBlock { Width = 20 });
            radioRow.Children.Add(_radioManual);
            Set(root, radioRow, 2);

            // ── Library panel ─────────────────────────────────────────────────
            _libraryPanel = new Grid();
            var libGrid = (Grid)_libraryPanel;
            libGrid.RowDefinitions.Add(Row(GridUnitType.Auto));
            libGrid.RowDefinitions.Add(Row(GridUnitType.Star));

            var filterRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
            };
            filterRow.Children.Add(new TextBlock
            {
                Text = "Material:",
                Foreground = Brush(200, 200, 200),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            _materialFilter = new ComboBox
            {
                Width = 200, Height = 26,
                Background = Brush(37, 37, 38),
                Foreground = Brushes.Black,
                BorderBrush = Brush(67, 67, 70),
            };
            _materialFilter.SelectionChanged += OnMaterialFilterChanged;
            filterRow.Children.Add(_materialFilter);
            Set(libGrid, filterRow, 0);

            _platesGrid = BuildPlatesGrid();
            Set(libGrid, _platesGrid, 1);

            Set(root, _libraryPanel, 3);

            // ── Manual panel ──────────────────────────────────────────────────
            _manualPanel = new StackPanel { Visibility = Visibility.Collapsed };
            _manualPanel.Children.Add(InputRow("Plate Width (mm):",  ref _widthBox,  "2440"));
            _manualPanel.Children.Add(InputRow("Plate Height (mm):", ref _heightBox, "1220"));

            Set(root, _manualPanel, 3);

            // ── Spacing (always visible) ──────────────────────────────────────
            var spacingSection = InputRow("Part Spacing (mm):", ref _spacingBox, "10");
            spacingSection.Margin = new Thickness(0, 8, 0, 0);
            Set(root, spacingSection, 4);

            // ── Buttons ───────────────────────────────────────────────────────
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0),
            };
            var ok = DlgBtn("Create Nesting", true,  Brush(0, 122, 204), OnOk);
            var cancel = DlgBtn("Cancel",     false, Brush(70, 70, 75),  (s, e) => { DialogResult = false; Close(); });
            btnRow.Children.Add(cancel);
            btnRow.Children.Add(ok);
            Set(root, btnRow, 5);

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
                RowHeaderWidth = 0,
                Margin = new Thickness(0, 0, 0, 0),
            };

            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty,      Brush(55, 55, 60)));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty,      new SolidColorBrush(Color.FromRgb(200, 200, 200))));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty,     Brush(75, 75, 80)));
            headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty,          new Thickness(8, 5, 8, 5)));
            headerStyle.Setters.Add(new Setter(TextElement.FontWeightProperty,  FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(TextElement.FontSizeProperty,    11.0));
            grid.ColumnHeaderStyle = headerStyle;

            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            var selCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selCell.Setters.Add(new Setter(Control.BackgroundProperty, Brush(0, 102, 180)));
            selCell.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            cellStyle.Triggers.Add(selCell);
            grid.CellStyle = cellStyle;

            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            var selRow = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            selRow.Setters.Add(new Setter(Control.BackgroundProperty, Brush(0, 102, 180)));
            selRow.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            rowStyle.Triggers.Add(selRow);
            grid.RowStyle = rowStyle;

            grid.Columns.Add(Col("Name",          "Code",             new DataGridLength(1, DataGridLengthUnitType.Star)));
            grid.Columns.Add(Col("Material",      "MaterialName",     new DataGridLength(1, DataGridLengthUnitType.Star)));
            grid.Columns.Add(Col("Dimensions",    "DimensionsText",   140));
            grid.Columns.Add(Col("Thickness (mm)","ThicknessText",     100));

            grid.SelectionChanged += OnPlateSelectionChanged;
            grid.MouseDoubleClick += (s, e) => { if (_platesGrid.SelectedItem != null) OnOk(s, e); };

            return grid;
        }

        // ── Event handlers ─────────────────────────────────────────────────────

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadPlates();
            SwitchMode(_useLibrary);
        }

        private void LoadPlates()
        {
            try
            {
                _allPlates = MaterialLibraryService.GetPlatesForCurrentProject();
            }
            catch
            {
                _allPlates = new List<MaterialPlate>();
            }

            // If no plates, default to manual mode.
            if (_allPlates.Count == 0)
            {
                _radioLibrary.IsEnabled = false;
                _radioManual.IsChecked  = true;
                return;
            }

            // Populate material filter: "All Materials" + distinct names.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _materialFilter.Items.Add(new ComboBoxItem { Content = "All Materials", Tag = null, Foreground = Brushes.Black });
            foreach (var plate in _allPlates)
            {
                string name = plate.MaterialName ?? "";
                if (seen.Add(name))
                    _materialFilter.Items.Add(new ComboBoxItem { Content = name, Tag = name, Foreground = Brushes.Black });
            }
            _materialFilter.SelectedIndex = 0;

            ApplyMaterialFilter(null);

            // Pre-select default plate if any.
            foreach (PlateRow row in _platesGrid.Items)
            {
                if (row.Plate.IsDefault) { _platesGrid.SelectedItem = row; break; }
            }
        }

        private void OnMaterialFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = _materialFilter.SelectedItem as ComboBoxItem;
            ApplyMaterialFilter(item?.Tag as string);
        }

        private void ApplyMaterialFilter(string materialName)
        {
            var rows = new List<PlateRow>();
            foreach (var p in _allPlates)
            {
                if (materialName == null || p.MaterialName == materialName)
                    rows.Add(new PlateRow(p));
            }
            _platesGrid.ItemsSource = rows;
        }

        private void OnPlateSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No action needed here — we read SelectedItem on OK.
        }

        private void SwitchMode(bool library)
        {
            _useLibrary = library;
            _radioLibrary.IsChecked = library;
            _radioManual.IsChecked  = !library;
            _libraryPanel.Visibility = library ? Visibility.Visible   : Visibility.Collapsed;
            _manualPanel.Visibility  = library ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(_spacingBox.Text, out double spacing) || spacing < 0)
            {
                Warn("Enter a valid part spacing (0 or greater).");
                _spacingBox.Focus(); return;
            }

            if (_useLibrary)
            {
                var row = _platesGrid.SelectedItem as PlateRow;
                if (row == null)
                {
                    Warn("Select a plate from the library, or switch to manual entry.");
                    return;
                }
                SelectedPlate    = row.Plate;
                PlateWidth       = row.Plate.Width;
                PlateHeight      = row.Plate.Height;
                PlateThickness   = row.Plate.Thickness;
            }
            else
            {
                if (!double.TryParse(_widthBox.Text,  out double w) || w <= 0)
                { Warn("Enter a valid plate width."); _widthBox.Focus(); return; }
                if (!double.TryParse(_heightBox.Text, out double h) || h <= 0)
                { Warn("Enter a valid plate height."); _heightBox.Focus(); return; }
                PlateWidth  = w;
                PlateHeight = h;
            }

            Spacing = spacing;
            DialogResult = true;
            Close();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static SolidColorBrush Brush(byte r, byte g, byte b)
            => new SolidColorBrush(Color.FromRgb(r, g, b));

        private static RowDefinition Row(GridUnitType t, double value = 1)
            => new RowDefinition { Height = new GridLength(value, t) };

        private static void Set(Grid g, UIElement el, int row)
        { Grid.SetRow(el, row); g.Children.Add(el); }

        private static RadioButton Radio(string text, bool isChecked) => new RadioButton
        {
            Content = text, IsChecked = isChecked,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        private StackPanel InputRow(string label, ref TextBox box, string defaultValue)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Margin = new Thickness(0, 0, 0, 4),
            });
            box = new TextBox
            {
                Text = defaultValue, Height = 28,
                Padding = new Thickness(6, 3, 6, 3),
                Background = Brush(37, 37, 38),
                Foreground = Brushes.White,
                BorderBrush = Brush(67, 67, 70),
                BorderThickness = new Thickness(1),
                FontSize = 14,
            };
            panel.Children.Add(box);
            return panel;
        }

        private static DataGridTextColumn Col(string header, string binding, DataGridLength width)
            => new DataGridTextColumn
            {
                Header  = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width   = width,
            };
        private static DataGridTextColumn Col(string header, string binding, double px)
            => Col(header, binding, new DataGridLength(px));

        private static Button DlgBtn(string text, bool isDefault, SolidColorBrush bg, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = text, Height = 28, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(6, 0, 0, 0),
                Background = bg, Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = isDefault ? FontWeights.SemiBold : FontWeights.Normal,
                IsDefault = isDefault,
            };
            b.Click += click;
            return b;
        }

        private void Warn(string msg) =>
            MessageBox.Show(msg, "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);

        // ── View model ────────────────────────────────────────────────────────

        private class PlateRow
        {
            public readonly MaterialPlate Plate;
            public string Code           => Plate.Code;
            public string MaterialName   => Plate.MaterialName ?? "";
            public string DimensionsText => $"{Plate.Width:0.##} × {Plate.Height:0.##}";
            public string ThicknessText  => $"{Plate.Thickness:0.##}";
            public PlateRow(MaterialPlate p) { Plate = p; }
        }
    }
}
