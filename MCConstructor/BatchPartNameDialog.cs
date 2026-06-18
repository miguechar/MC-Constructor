using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MCConstructor
{
    /// <summary>
    /// Popup for batch-naming parts with an optional shared material assignment.
    /// Each entity gets PartName = "{prefix}-{serial}" with auto-incrementing serial.
    /// </summary>
    public class BatchPartNameDialog : Window
    {
        private TextBox _prefixBox;
        private TextBox _startSerialBox;
        private Label _previewLabel;
        private ComboBox _materialCombo;
        private readonly int _selectedCount;
        private readonly List<Material> _materials;

        public string   Prefix          { get; private set; }
        public int      StartingSerial  { get; private set; } = 100;
        public Material SelectedMaterial { get; private set; }

        public BatchPartNameDialog(int selectedCount, List<Material> materials = null)
        {
            _selectedCount = selectedCount;
            _materials     = materials ?? new List<Material>();

            Title = "Batch Add Part Names";
            Width = 440;
            Height = _materials.Count > 0 ? 360 : 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));

            var stack = new StackPanel { Margin = new Thickness(20) };

            stack.Children.Add(new Label
            {
                Content = "Batch Add Part Names",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6),
            });

            stack.Children.Add(new Label
            {
                Content = $"Selected objects: {selectedCount}",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 80)),
                Margin = new Thickness(0, 0, 0, 12),
            });

            // Prefix
            stack.Children.Add(FieldLabel("Prefix (e.g. A1LB1-00-D07):"));
            _prefixBox = InputBox("");
            _prefixBox.TextChanged += (s, e) => UpdatePreview();
            stack.Children.Add(_prefixBox);

            // Starting serial
            stack.Children.Add(FieldLabel("Starting Serial:"));
            _startSerialBox = InputBox("100");
            _startSerialBox.TextChanged += (s, e) => UpdatePreview();
            stack.Children.Add(_startSerialBox);

            // Material
            if (_materials.Count > 0)
            {
                stack.Children.Add(FieldLabel("Material (optional):"));
                _materialCombo = new ComboBox
                {
                    Height = 28,
                    Margin = new Thickness(0, 4, 0, 8),
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

            // Preview
            _previewLabel = new Label
            {
                Foreground = new SolidColorBrush(Color.FromRgb(150, 200, 255)),
                FontStyle = FontStyles.Italic,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
            };
            stack.Children.Add(_previewLabel);

            // Buttons
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0),
            };
            var applyBtn = new Button
            {
                Content = "Apply", Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold, IsDefault = true,
            };
            applyBtn.Click += OnApply;
            btnRow.Children.Add(applyBtn);

            var cancelBtn = new Button
            {
                Content = "Cancel", Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Background = new SolidColorBrush(Color.FromRgb(70, 70, 75)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                IsCancel = true,
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            btnRow.Children.Add(cancelBtn);
            stack.Children.Add(btnRow);

            Content = stack;
            Loaded += (s, e) => { _prefixBox.Focus(); UpdatePreview(); };
        }

        private static Label FieldLabel(string text) => new Label
        {
            Content = text,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Padding = new Thickness(0, 0, 0, 2),
        };

        private static TextBox InputBox(string text) => new TextBox
        {
            Text = text, Height = 28,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(6, 3, 6, 3),
            Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(67, 67, 70)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
        };

        private void UpdatePreview()
        {
            if (_previewLabel == null) return;
            string prefix = _prefixBox.Text?.Trim() ?? "";
            if (!int.TryParse(_startSerialBox.Text, out int start))
            {
                _previewLabel.Content = "Preview: (enter a valid starting serial)";
                return;
            }
            if (_selectedCount <= 0) { _previewLabel.Content = "Preview: (no parts selected)"; return; }

            string first = BuildName(prefix, start);
            _previewLabel.Content = _selectedCount == 1
                ? $"Preview: {first}"
                : $"Preview: {first}  ...  {BuildName(prefix, start + _selectedCount - 1)}";
        }

        public static string BuildName(string prefix, int serial)
            => string.IsNullOrEmpty(prefix) ? serial.ToString() : $"{prefix}-{serial}";

        private void OnApply(object sender, RoutedEventArgs e)
        {
            string prefix = _prefixBox.Text?.Trim() ?? "";

            if (!int.TryParse(_startSerialBox.Text, out int start))
            {
                MessageBox.Show("Please enter a whole number for the starting serial.",
                    "Invalid Serial", MessageBoxButton.OK, MessageBoxImage.Warning);
                _startSerialBox.Focus(); return;
            }
            if (start < 0)
            {
                MessageBox.Show("Starting serial must be zero or greater.",
                    "Invalid Serial", MessageBoxButton.OK, MessageBoxImage.Warning);
                _startSerialBox.Focus(); return;
            }
            if (string.IsNullOrEmpty(prefix))
            {
                if (MessageBox.Show(
                    "No prefix entered — parts will be named with just the serial number.\n\nContinue?",
                    "Empty Prefix", MessageBoxButton.OKCancel, MessageBoxImage.Question)
                    != MessageBoxResult.OK)
                { _prefixBox.Focus(); return; }
            }

            Prefix = prefix;
            StartingSerial = start;
            if (_materialCombo?.SelectedItem is ComboBoxItem item && item.Tag is Material mat)
                SelectedMaterial = mat;
            DialogResult = true;
            Close();
        }
    }
}
