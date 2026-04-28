using System.Windows;
using System.Windows.Controls;

namespace FirstAcadPlugin
{
    /// <summary>
    /// A popup dialog for entering part metadata.
    /// This is a WPF Window created in code (no XAML needed).
    /// </summary>
    public class PartMetadataDialog : Window
    {
        // Text box for the part name input
        private TextBox partNameTextBox;

        // Property to get the entered part name after dialog closes
        public string PartName { get; private set; }

        public PartMetadataDialog()
        {
            // Window settings
            Title = "Add Part Metadata";
            Width = 350;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            // Create the main container
            var mainStack = new StackPanel
            {
                Margin = new Thickness(15)
            };

            // Part Name label
            var partNameLabel = new Label
            {
                Content = "Part Name:",
                FontWeight = FontWeights.Bold
            };
            mainStack.Children.Add(partNameLabel);

            // Part Name text box
            partNameTextBox = new TextBox
            {
                Height = 25,
                Margin = new Thickness(0, 5, 0, 15)
            };
            mainStack.Children.Add(partNameTextBox);

            // Buttons panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            // OK button
            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Height = 28,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true // Pressing Enter clicks this button
            };
            okButton.Click += OkButton_Click;
            buttonPanel.Children.Add(okButton);

            // Cancel button
            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 28,
                IsCancel = true // Pressing Escape clicks this button
            };
            cancelButton.Click += CancelButton_Click;
            buttonPanel.Children.Add(cancelButton);

            mainStack.Children.Add(buttonPanel);

            // Set the window content
            Content = mainStack;

            // Focus on the text box when window opens
            Loaded += (s, e) => partNameTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Save the entered part name
            PartName = partNameTextBox.Text.Trim();

            // Set dialog result to true (indicates OK was clicked)
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Set dialog result to false (indicates Cancel was clicked)
            DialogResult = false;
            Close();
        }
    }
}
