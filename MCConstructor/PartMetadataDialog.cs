using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Dialog for assigning a part name (and optional material) to a 3D solid.
    /// </summary>
    public class PartMetadataDialog : Window
    {
        private TextBox _partNameBox;
        private ComboBox _materialCombo;
        private readonly List<Material> _materials;

        public string   PartName         { get; private set; }
        public Material SelectedMaterial { get; private set; }

        public PartMetadataDialog(List<Material> materials = null)
        {
            _materials = materials ?? new List<Material>();

            Title = "Add Part Metadata";
            Width = 380;
            Height = _materials.Count > 0 ? 220 : 175;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));

            var stack = new StackPanel { Margin = new Thickness(16) };

            // Header
            stack.Children.Add(new Label
            {
                Content = "Add Part Metadata",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10),
            });

            // Part Name
            stack.Children.Add(new Label
            {
                Content = "Part Name:",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Padding = new Thickness(0),
            });
            _partNameBox = new TextBox
            {
                Height = 28,
                Margin = new Thickness(0, 4, 0, 12),
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(67, 67, 70)),
                BorderThickness = new Thickness(1),
                FontSize = 13,
            };
            stack.Children.Add(_partNameBox);

            // Material selector (only when materials are available)
            if (_materials.Count > 0)
            {
                stack.Children.Add(new Label
                {
                    Content = "Material (optional):",
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    Padding = new Thickness(0),
                });
                _materialCombo = new ComboBox
                {
                    Height = 28,
                    Margin = new Thickness(0, 4, 0, 12),
                    Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(67, 67, 70)),
                };
                _materialCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = null });
                foreach (var m in _materials)
                    _materialCombo.Items.Add(new ComboBoxItem { Content = m.Name, Tag = m });
                _materialCombo.SelectedIndex = 0;
                stack.Children.Add(_materialCombo);
            }

            // Buttons
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var okBtn = new Button
            {
                Content = "OK", Width = 80, Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold, IsDefault = true,
            };
            okBtn.Click += OnOk;
            btnRow.Children.Add(okBtn);

            var cancelBtn = new Button
            {
                Content = "Cancel", Width = 80, Height = 28,
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 65)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                IsCancel = true,
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            btnRow.Children.Add(cancelBtn);

            stack.Children.Add(btnRow);
            Content = stack;
            Loaded += (s, e) => _partNameBox.Focus();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            PartName = _partNameBox.Text.Trim();
            if (_materialCombo != null && _materialCombo.SelectedItem is ComboBoxItem item && item.Tag is Material mat)
                SelectedMaterial = mat;
            DialogResult = true;
            Close();
        }
    }
}
