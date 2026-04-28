using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Dialog for entering plate dimensions for nesting.
    /// </summary>
    public class NestingDialog : Window
    {
        private TextBox widthTextBox;
        private TextBox heightTextBox;
        private TextBox spacingTextBox;
        private Label partsInfoLabel;

        /// <summary>
        /// Plate width in mm.
        /// </summary>
        public double PlateWidth { get; private set; }

        /// <summary>
        /// Plate height in mm.
        /// </summary>
        public double PlateHeight { get; private set; }

        /// <summary>
        /// Spacing between parts in mm.
        /// </summary>
        public double Spacing { get; private set; } = 10.0;

        public NestingDialog(int selectedPartsCount)
        {
            // Window settings
            Title = "Create Nesting Layout";
            Width = 380;
            Height = 340;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));

            // Main container
            var mainGrid = new Grid { Margin = new Thickness(20) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            // Header
            var headerLabel = new Label
            {
                Content = "Plate Dimensions",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(headerLabel, 0);
            mainGrid.Children.Add(headerLabel);

            // Parts info
            partsInfoLabel = new Label
            {
                Content = $"Selected parts: {selectedPartsCount}",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 80)),
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(partsInfoLabel, 1);
            mainGrid.Children.Add(partsInfoLabel);

            // Width input
            var widthPanel = CreateInputRow("Plate Width (mm):", "2440");
            widthTextBox = widthPanel.Children[1] as TextBox;
            Grid.SetRow(widthPanel, 2);
            mainGrid.Children.Add(widthPanel);

            // Height input
            var heightPanel = CreateInputRow("Plate Height (mm):", "1220");
            heightTextBox = heightPanel.Children[1] as TextBox;
            Grid.SetRow(heightPanel, 3);
            mainGrid.Children.Add(heightPanel);

            // Spacing input
            var spacingPanel = CreateInputRow("Part Spacing (mm):", "10");
            spacingTextBox = spacingPanel.Children[1] as TextBox;
            Grid.SetRow(spacingPanel, 4);
            mainGrid.Children.Add(spacingPanel);

            // Common plate sizes info
            var infoLabel = new Label
            {
                Content = "Common sizes: 2440x1220 (8'x4'), 3000x1500, 6000x2000",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(infoLabel, 5);
            mainGrid.Children.Add(infoLabel);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var createBtn = new Button
            {
                Content = "Create Nesting",
                Width = 120,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                IsDefault = true
            };
            createBtn.Click += CreateBtn_Click;
            buttonPanel.Children.Add(createBtn);

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 65)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                IsCancel = true
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelBtn);

            Grid.SetRow(buttonPanel, 6);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;

            // Focus on width field
            Loaded += (s, e) => widthTextBox.Focus();
        }

        private StackPanel CreateInputRow(string labelText, string defaultValue)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var label = new Label
            {
                Content = labelText,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Padding = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(label);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Height = 28,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(67, 67, 70)),
                BorderThickness = new Thickness(1),
                FontSize = 14
            };
            panel.Children.Add(textBox);

            return panel;
        }

        private void CreateBtn_Click(object sender, RoutedEventArgs e)
        {
            // Validate inputs
            if (!double.TryParse(widthTextBox.Text, out double width) || width <= 0)
            {
                MessageBox.Show("Please enter a valid plate width.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                widthTextBox.Focus();
                return;
            }

            if (!double.TryParse(heightTextBox.Text, out double height) || height <= 0)
            {
                MessageBox.Show("Please enter a valid plate height.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                heightTextBox.Focus();
                return;
            }

            if (!double.TryParse(spacingTextBox.Text, out double spacing) || spacing < 0)
            {
                MessageBox.Show("Please enter a valid spacing (0 or greater).", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                spacingTextBox.Focus();
                return;
            }

            PlateWidth = width;
            PlateHeight = height;
            Spacing = spacing;

            DialogResult = true;
            Close();
        }
    }
}
