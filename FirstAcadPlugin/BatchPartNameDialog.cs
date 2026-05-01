using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Popup for batch-naming parts. Asks for a prefix that gets followed
    /// by a dash and a serial number, e.g. user enters
    /// "A1LB1-00-D07" with start=100 and 2 selected parts -&gt;
    /// "A1LB1-00-D07-100", "A1LB1-00-D07-101".
    ///
    /// The starting serial defaults to 100 because that's the workflow the
    /// user described, but it's editable so they can resume a numbering
    /// run that was already partially used.
    /// </summary>
    public class BatchPartNameDialog : Window
    {
        private TextBox prefixTextBox;
        private TextBox startSerialTextBox;
        private Label previewLabel;
        private readonly int selectedCount;

        /// <summary>The prefix entered by the user. Trimmed of whitespace.</summary>
        public string Prefix { get; private set; }

        /// <summary>First serial in the run. Subsequent parts increment by 1.</summary>
        public int StartingSerial { get; private set; } = 100;

        public BatchPartNameDialog(int selectedCount)
        {
            this.selectedCount = selectedCount;

            Title = "Batch Add Part Names";
            Width = 420;
            Height = 290;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));

            var mainStack = new StackPanel { Margin = new Thickness(20) };

            // Header
            mainStack.Children.Add(new Label
            {
                Content = "Batch Add Part Names",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Selection count
            mainStack.Children.Add(new Label
            {
                Content = $"Selected objects: {selectedCount}",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 80)),
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Prefix input
            mainStack.Children.Add(new Label
            {
                Content = "Prefix (e.g. A1LB1-00-D07):",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Padding = new Thickness(0, 0, 0, 4)
            });
            prefixTextBox = MakeTextBox("");
            prefixTextBox.TextChanged += (s, e) => UpdatePreview();
            mainStack.Children.Add(prefixTextBox);

            // Starting serial input
            mainStack.Children.Add(new Label
            {
                Content = "Starting Serial:",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Padding = new Thickness(0, 8, 0, 4)
            });
            startSerialTextBox = MakeTextBox("100");
            startSerialTextBox.TextChanged += (s, e) => UpdatePreview();
            mainStack.Children.Add(startSerialTextBox);

            // Live preview - tells the user exactly what names they'll get
            // so there's no surprise with the serial padding / dash.
            previewLabel = new Label
            {
                Foreground = new SolidColorBrush(Color.FromRgb(150, 200, 255)),
                FontStyle = FontStyles.Italic,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0)
            };
            mainStack.Children.Add(previewLabel);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var okButton = new Button
            {
                Content = "Apply",
                Width = 100,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                IsDefault = true
            };
            okButton.Click += OkButton_Click;
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 65)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                IsCancel = true
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelButton);

            mainStack.Children.Add(buttonPanel);

            Content = mainStack;
            Loaded += (s, e) => { prefixTextBox.Focus(); UpdatePreview(); };
        }

        private TextBox MakeTextBox(string defaultText)
        {
            return new TextBox
            {
                Text = defaultText,
                Height = 28,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(67, 67, 70)),
                BorderThickness = new Thickness(1),
                FontSize = 14
            };
        }

        /// <summary>
        /// Show the first and last names that will be applied. Helps the
        /// user catch typos in the prefix or a wrong starting serial before
        /// they commit to renaming a big selection.
        /// </summary>
        private void UpdatePreview()
        {
            if (previewLabel == null) return;

            string prefix = prefixTextBox.Text?.Trim() ?? "";
            if (!int.TryParse(startSerialTextBox.Text, out int start))
            {
                previewLabel.Content = "Preview: (enter a valid starting serial)";
                return;
            }

            if (selectedCount <= 0)
            {
                previewLabel.Content = "Preview: (no parts selected)";
                return;
            }

            string firstName = BuildName(prefix, start);
            if (selectedCount == 1)
            {
                previewLabel.Content = $"Preview: {firstName}";
            }
            else
            {
                string lastName = BuildName(prefix, start + selectedCount - 1);
                previewLabel.Content = $"Preview: {firstName}  ...  {lastName}";
            }
        }

        /// <summary>
        /// Compose a part name from a prefix and a serial. Mirrors the
        /// logic the command will use, kept in one place so the dialog
        /// preview is always accurate.
        /// </summary>
        public static string BuildName(string prefix, int serial)
        {
            // No prefix -> just the serial. Avoids dangling dashes when the
            // user clears the prefix field.
            if (string.IsNullOrEmpty(prefix))
                return serial.ToString();
            return $"{prefix}-{serial}";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string prefix = prefixTextBox.Text?.Trim() ?? "";

            if (!int.TryParse(startSerialTextBox.Text, out int start))
            {
                MessageBox.Show(
                    "Please enter a whole number for the starting serial.",
                    "Invalid Serial",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                startSerialTextBox.Focus();
                return;
            }

            if (start < 0)
            {
                MessageBox.Show(
                    "Starting serial must be zero or greater.",
                    "Invalid Serial",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                startSerialTextBox.Focus();
                return;
            }

            // Empty prefix is allowed (you get pure-numeric names) but warn
            // the user so they don't apply it accidentally.
            if (string.IsNullOrEmpty(prefix))
            {
                var answer = MessageBox.Show(
                    "No prefix entered - parts will be named with just the serial number (e.g. \"100\", \"101\").\n\nContinue?",
                    "Empty Prefix",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK)
                {
                    prefixTextBox.Focus();
                    return;
                }
            }

            Prefix = prefix;
            StartingSerial = start;
            DialogResult = true;
            Close();
        }
    }
}
