using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace FirstAcadPlugin
{
    public class InsertProfileDialog : Window
    {
        private ListBox _profileListBox;
        private TextBlock _pathLabel;
        private TextBox _lengthBox;

        public Drawing SelectedDrawing { get; private set; }
        public double Length { get; private set; }

        public InsertProfileDialog(List<Drawing> profileDrawings)
        {
            Title = "Insert Profile";
            Width = 440;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 320;
            MinHeight = 300;

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            // Header
            var header = new Label
            {
                Content = "Select a profile cross-section:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(10, 10, 10, 5)
            };
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Profile list
            _profileListBox = new ListBox
            {
                Margin = new Thickness(10, 0, 10, 5),
                DisplayMemberPath = "Name"
            };
            foreach (var d in profileDrawings)
                _profileListBox.Items.Add(d);

            _profileListBox.SelectionChanged += OnSelectionChanged;
            _profileListBox.MouseDoubleClick += (s, e) =>
            {
                if (_profileListBox.SelectedItem != null)
                    TryAccept();
            };
            Grid.SetRow(_profileListBox, 1);
            mainGrid.Children.Add(_profileListBox);

            // Path info
            _pathLabel = new TextBlock
            {
                Margin = new Thickness(12, 2, 10, 6),
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
                Text = ""
            };
            Grid.SetRow(_pathLabel, 2);
            mainGrid.Children.Add(_pathLabel);

            // Length input row
            var lengthRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(10, 0, 10, 10)
            };

            lengthRow.Children.Add(new Label
            {
                Content = "Length:",
                Width = 60,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0)
            });

            _lengthBox = new TextBox
            {
                Width = 120,
                Height = 24,
                Text = "1000",
                VerticalAlignment = VerticalAlignment.Center
            };
            lengthRow.Children.Add(_lengthBox);

            lengthRow.Children.Add(new TextBlock
            {
                Text = " (drawing units)",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(4, 0, 0, 0)
            });

            Grid.SetRow(lengthRow, 3);
            mainGrid.Children.Add(lengthRow);

            // Buttons
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10, 0, 10, 10)
            };

            var okBtn = new Button
            {
                Content = "Insert",
                Width = 80,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okBtn.Click += (s, e) => TryAccept();
            buttonRow.Children.Add(okBtn);

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 28,
                IsCancel = true
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            buttonRow.Children.Add(cancelBtn);

            Grid.SetRow(buttonRow, 4);
            mainGrid.Children.Add(buttonRow);

            Content = mainGrid;

            if (_profileListBox.Items.Count > 0)
                _profileListBox.SelectedIndex = 0;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_profileListBox.SelectedItem is Drawing d)
                _pathLabel.Text = d.FilePath;
            else
                _pathLabel.Text = "";
        }

        private void TryAccept()
        {
            if (!(_profileListBox.SelectedItem is Drawing selected))
            {
                MessageBox.Show("Please select a profile.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(_lengthBox.Text, out double len) || len <= 0)
            {
                MessageBox.Show("Enter a positive number for length.", "Invalid Length",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedDrawing = selected;
            Length = len;
            DialogResult = true;
            Close();
        }
    }
}
