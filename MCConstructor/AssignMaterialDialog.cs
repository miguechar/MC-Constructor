using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MCConstructor
{
    /// <summary>
    /// Compact dialog for assigning a material grade to multiple selected entities.
    /// </summary>
    public class AssignMaterialDialog : Window
    {
        public Material SelectedMaterial { get; private set; }

        private readonly ComboBox _materialCombo;

        public AssignMaterialDialog(int entityCount, List<Material> materials)
        {
            Title  = "Assign Material";
            Width  = 360;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = Brush(45, 45, 48);
            DialogTheme.Apply(this);

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // info
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // label
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // combo
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons

            // Header
            var header = new TextBlock
            {
                Text = "Assign Material",
                FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10),
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Info
            var info = new TextBlock
            {
                Text = $"Applying to {entityCount} selected object{(entityCount == 1 ? "" : "s")}",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 100)),
                Margin = new Thickness(0, 0, 0, 14),
                FontSize = 12,
            };
            Grid.SetRow(info, 1);
            root.Children.Add(info);

            // Label
            var lbl = new TextBlock
            {
                Text = "Material:",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 12,
            };
            Grid.SetRow(lbl, 2);
            root.Children.Add(lbl);

            // ComboBox
            _materialCombo = new ComboBox
            {
                Height = 28, Margin = new Thickness(0, 0, 0, 0),
                Background = Brush(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = Brush(67, 67, 70),
            };
            if (materials.Count == 0)
            {
                _materialCombo.Items.Add(new ComboBoxItem
                {
                    Content = "(No materials in project library)",
                    Tag = null,
                    IsEnabled = false,
                });
            }
            else
            {
                foreach (var m in materials)
                {
                    string dens = m.Density.ToString("G6", CultureInfo.InvariantCulture);
                    _materialCombo.Items.Add(new ComboBoxItem { Content = $"{m.Name}  —  {dens} kg/mm³", Tag = m });
                }
            }
            _materialCombo.SelectedIndex = 0;
            Grid.SetRow(_materialCombo, 3);
            root.Children.Add(_materialCombo);

            // Buttons
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            Grid.SetRow(btnRow, 5);
            root.Children.Add(btnRow);
            btnRow.Children.Add(DlgBtn("Cancel", false, (s, e) => { DialogResult = false; Close(); }));
            btnRow.Children.Add(DlgBtn("Assign", true,  OnAssign));

            Content = root;
        }

        private void OnAssign(object sender, RoutedEventArgs e)
        {
            if (_materialCombo.SelectedItem is ComboBoxItem item && item.Tag is Material mat)
            {
                SelectedMaterial = mat;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a material.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static Button DlgBtn(string text, bool isDefault, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = text, Height = 28, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(6, 0, 0, 0),
                Background = isDefault ? Brush(0, 122, 204) : Brush(70, 70, 75),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontWeight = isDefault ? FontWeights.SemiBold : FontWeights.Normal,
                IsDefault = isDefault, IsCancel = !isDefault,
            };
            b.Click += click;
            return b;
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b)
            => new SolidColorBrush(Color.FromRgb(r, g, b));
    }
}
