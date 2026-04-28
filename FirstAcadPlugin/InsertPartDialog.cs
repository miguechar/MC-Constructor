using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Dialog for selecting a part to insert from the database.
    /// </summary>
    public class InsertPartDialog : Window
    {
        private ListBox partListBox;

        /// <summary>
        /// The selected part to insert.
        /// </summary>
        public DrawingPart SelectedPart { get; private set; }

        public InsertPartDialog(List<DrawingPart> availableParts)
        {
            // Window settings
            Title = "Insert Part";
            Width = 400;
            Height = 450;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 300;
            MinHeight = 300;

            // Create the main container
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            // Header
            var headerLabel = new Label
            {
                Content = "Select a part to insert:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(10, 10, 10, 5)
            };
            Grid.SetRow(headerLabel, 0);
            mainGrid.Children.Add(headerLabel);

            // Parts list
            partListBox = new ListBox
            {
                Margin = new Thickness(10, 0, 10, 10),
                DisplayMemberPath = "PartName"
            };

            foreach (var part in availableParts)
            {
                partListBox.Items.Add(part);
            }

            partListBox.MouseDoubleClick += (s, e) =>
            {
                if (partListBox.SelectedItem != null)
                    OkButton_Click(s, e);
            };

            Grid.SetRow(partListBox, 1);
            mainGrid.Children.Add(partListBox);

            // Part details panel
            var detailsPanel = new StackPanel
            {
                Margin = new Thickness(10, 0, 10, 10),
                Visibility = Visibility.Collapsed
            };

            var detailsLabel = new TextBlock
            {
                Name = "DetailsLabel",
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray
            };
            detailsPanel.Children.Add(detailsLabel);

            partListBox.SelectionChanged += (s, e) =>
            {
                if (partListBox.SelectedItem is DrawingPart part)
                {
                    detailsPanel.Visibility = Visibility.Visible;
                    detailsLabel.Text = $"Source: {part.SourceDrawingName}\nType: {part.GeometryType}";
                }
                else
                {
                    detailsPanel.Visibility = Visibility.Collapsed;
                }
            };

            // Buttons panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            // OK button
            var okButton = new Button
            {
                Content = "Insert",
                Width = 80,
                Height = 28,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            okButton.Click += OkButton_Click;
            buttonPanel.Children.Add(okButton);

            // Cancel button
            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 28,
                IsCancel = true
            };
            cancelButton.Click += CancelButton_Click;
            buttonPanel.Children.Add(cancelButton);

            // Bottom panel with details and buttons
            var bottomPanel = new StackPanel();
            bottomPanel.Children.Add(detailsPanel);
            bottomPanel.Children.Add(buttonPanel);
            Grid.SetRow(bottomPanel, 2);
            mainGrid.Children.Add(bottomPanel);

            Content = mainGrid;

            // Select first item if available
            if (partListBox.Items.Count > 0)
                partListBox.SelectedIndex = 0;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (partListBox.SelectedItem is DrawingPart part)
            {
                SelectedPart = part;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a part to insert.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
