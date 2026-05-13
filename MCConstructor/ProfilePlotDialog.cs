using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MCConstructor
{
    public class ProfilePlotDialog : Window
    {
        private ListBox  _list;
        private TextBlock _pathLabel;
        private TextBox  _lengthBox;
        private TextBox  _nameBox;

        public Drawing SelectedDrawing { get; private set; }
        public double  Length          { get; private set; }
        public string  PlotName        { get; private set; }

        public ProfilePlotDialog(List<Drawing> profileDrawings, double suggestedLength = 0)
        {
            Title  = "Create Profile Plot";
            Width  = 460;
            Height = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 340;
            MinHeight = 360;
            Background = D(45, 45, 48);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            // Header
            Grid.SetRow(AddLabel(grid, "Select profile cross-section:", FontWeights.Bold,
                new Thickness(10, 10, 10, 4)), 0);

            // List
            _list = new ListBox
            {
                Margin = new Thickness(10, 0, 10, 4), DisplayMemberPath = "Name",
                Background = D(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D(67, 67, 70),
            };
            foreach (var d in profileDrawings) _list.Items.Add(d);
            _list.SelectionChanged += (s, e) =>
            {
                if (_list.SelectedItem is Drawing d)
                {
                    _pathLabel.Text = d.FilePath;
                    if (string.IsNullOrWhiteSpace(_nameBox.Text) || _nameBox.Tag as bool? == false)
                        _nameBox.Text = d.Name + "_PLOT";
                }
                else _pathLabel.Text = "";
            };
            _list.MouseDoubleClick += (s, e) => { if (_list.SelectedItem != null) TryAccept(); };
            Grid.SetRow(_list, 1);
            grid.Children.Add(_list);

            // Path label
            _pathLabel = new TextBlock
            {
                Margin = new Thickness(12, 0, 10, 8),
                TextWrapping = TextWrapping.Wrap,
                Foreground = D(140, 140, 145),
                FontSize = 11
            };
            Grid.SetRow(_pathLabel, 2);
            grid.Children.Add(_pathLabel);

            // Length row
            Grid.SetRow(MakeFieldRow(grid, "Length (mm):", out _lengthBox,
                suggestedLength > 0 ? suggestedLength.ToString("F0") : "1000",
                new Thickness(10, 0, 10, 6)), 3);

            // Plot name row
            Grid.SetRow(MakeFieldRow(grid, "Plot Name:", out _nameBox, "",
                new Thickness(10, 0, 10, 10)), 4);
            _nameBox.TextChanged += (s, e) => { _nameBox.Tag = true; };

            // Buttons
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10, 0, 10, 10)
            };
            var ok = new Button
            {
                Content = "Create", Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0), IsDefault = true,
                Background = D(0, 122, 204), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold,
            };
            ok.Click += (s, e) => TryAccept();
            var cancel = new Button
            {
                Content = "Cancel", Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                IsCancel = true,
                Background = D(70, 70, 75), Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnRow.Children.Add(ok);
            btnRow.Children.Add(cancel);
            Grid.SetRow(btnRow, 5);
            grid.Children.Add(btnRow);

            Content = grid;

            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private void TryAccept()
        {
            if (!(_list.SelectedItem is Drawing d))
            {
                MessageBox.Show("Select a profile drawing.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(_lengthBox.Text, out double len) || len <= 0)
            {
                MessageBox.Show("Enter a positive number for length.", "Invalid Length",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string name = _nameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Enter a plot name.", "Name Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedDrawing = d;
            Length   = len;
            PlotName = name;
            DialogResult = true;
            Close();
        }

        // ---- layout helpers ----

        private static SolidColorBrush D(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

        private static Label AddLabel(Grid grid, string text, FontWeight weight, Thickness margin)
        {
            var lbl = new Label
            {
                Content = text, FontWeight = weight, Margin = margin,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Background = D(45, 45, 48),
            };
            grid.Children.Add(lbl);
            return lbl;
        }

        private static StackPanel MakeFieldRow(Grid grid, string label, out TextBox box,
            string defaultText, Thickness margin)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin };
            row.Children.Add(new Label
            {
                Content = label, Width = 110,
                VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Background = D(45, 45, 48),
            });
            box = new TextBox
            {
                Width = 200, Height = 28,
                Text = defaultText,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 3, 6, 3),
                Background = D(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
            };
            row.Children.Add(box);
            grid.Children.Add(row);
            return row;
        }
    }
}
