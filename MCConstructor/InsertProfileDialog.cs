using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
            Background = D(45, 45, 48);

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
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Background = D(45, 45, 48),
                Margin = new Thickness(10, 10, 10, 5)
            };
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Profile list
            _profileListBox = new ListBox
            {
                Margin = new Thickness(10, 0, 10, 5),
                DisplayMemberPath = "Name",
                Background = D(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D(67, 67, 70),
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
                Foreground = D(140, 140, 145),
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
                Padding = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Background = D(45, 45, 48),
            });

            _lengthBox = new TextBox
            {
                Width = 120, Height = 24,
                Text = "1000",
                VerticalAlignment = VerticalAlignment.Center,
                Background = D(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
            };
            lengthRow.Children.Add(_lengthBox);

            lengthRow.Children.Add(new TextBlock
            {
                Text = " (drawing units)",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = D(140, 140, 145),
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
                Width = 80, Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true,
                Background = D(0, 122, 204), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold,
            };
            okBtn.Click += (s, e) => TryAccept();
            buttonRow.Children.Add(okBtn);

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80, Height = 28,
                IsCancel = true,
                Background = D(60, 60, 65), Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            buttonRow.Children.Add(cancelBtn);

            Grid.SetRow(buttonRow, 4);
            mainGrid.Children.Add(buttonRow);

            Content = mainGrid;

            if (_profileListBox.Items.Count > 0)
                _profileListBox.SelectedIndex = 0;
        }

        private static SolidColorBrush D(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

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
