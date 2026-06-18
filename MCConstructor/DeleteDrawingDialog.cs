using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MCConstructor
{
    public class DeleteDrawingDialog : Window
    {
        private readonly List<Drawing> _drawings;
        private ListBox _list;
        private TextBlock _details;
        private Button _deleteButton;

        public Drawing SelectedDrawing { get; private set; }

        public DeleteDrawingDialog(Project project, List<Drawing> drawings)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            _drawings = drawings ?? new List<Drawing>();

            Title = $"Delete Drawing - {project.Name}";
            Width = 760;
            Height = 520;
            MinWidth = 620;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = D(45, 45, 48);
            DialogTheme.Apply(this);

            BuildUI(project);
            LoadDrawings();
        }

        private void BuildUI(Project project)
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = $"Project: {project.Name}\nRoot:    {project.Directory}",
                FontSize = 12,
                Foreground = D(160, 160, 165),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            _list = new ListBox
            {
                Background = D(37, 37, 38),
                Foreground = Brushes.White,
                BorderBrush = D(67, 67, 70),
                FontSize = 12
            };
            _list.SelectionChanged += List_SelectionChanged;
            _list.MouseDoubleClick += (s, e) => AcceptSelection();
            Grid.SetColumn(_list, 0);
            body.Children.Add(_list);

            var splitter = new GridSplitter
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent
            };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);

            _details = new TextBlock
            {
                Foreground = D(190, 190, 195),
                Background = D(37, 37, 38),
                Padding = new Thickness(10),
                TextWrapping = TextWrapping.Wrap,
                Text = "Select a drawing to delete."
            };
            Grid.SetColumn(_details, 2);
            body.Children.Add(_details);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var footer = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8, 0, 0) };
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            _deleteButton = new Button
            {
                Content = "Delete",
                Width = 88,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = false,
                IsDefault = true,
                Background = D(185, 65, 55),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold
            };
            _deleteButton.Click += (s, e) => AcceptSelection();
            buttonPanel.Children.Add(_deleteButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 88,
                Height = 28,
                IsCancel = true,
                Background = D(70, 70, 75),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            cancelButton.Click += (s, e) => Close();
            buttonPanel.Children.Add(cancelButton);

            DockPanel.SetDock(buttonPanel, Dock.Right);
            footer.Children.Add(buttonPanel);

            var note = new TextBlock
            {
                Text = "This removes the drawing file, drawing registration, and parts sourced from that drawing.",
                Foreground = D(160, 160, 165),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            footer.Children.Add(note);

            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        private void LoadDrawings()
        {
            _list.Items.Clear();

            foreach (var drawing in _drawings.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                _list.Items.Add(new DrawingItem(drawing));

            if (_drawings.Count == 0)
                _details.Text = "No drawings are registered in this project.";
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = _list.SelectedItem as DrawingItem;
            SelectedDrawing = item?.Drawing;
            _deleteButton.IsEnabled = SelectedDrawing != null;

            if (SelectedDrawing == null)
            {
                _details.Text = "Select a drawing to delete.";
                return;
            }

            var d = SelectedDrawing;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Name: {d.Name}");
            sb.AppendLine($"Type: {DrawingTypes.DisplayName(d.DrawingType)}");
            if (!string.IsNullOrEmpty(d.Discipline))
                sb.AppendLine($"Discipline: {d.Discipline}");
            if (!string.IsNullOrEmpty(d.Description))
                sb.AppendLine($"Notes: {d.Description}");
            sb.AppendLine();
            sb.AppendLine($"Path: {d.FilePath}");
            sb.AppendLine(File.Exists(d.FilePath) ? "File: found on disk" : "File: missing on disk");
            _details.Text = sb.ToString();
        }

        private void AcceptSelection()
        {
            if (SelectedDrawing == null) return;
            DialogResult = true;
            Close();
        }

        private static SolidColorBrush D(byte r, byte g, byte b)
            => new SolidColorBrush(Color.FromRgb(r, g, b));

        private class DrawingItem
        {
            public Drawing Drawing { get; }

            public DrawingItem(Drawing drawing)
            {
                Drawing = drawing;
            }

            public override string ToString()
            {
                string type = DrawingTypes.DisplayName(Drawing.DrawingType);
                string status = File.Exists(Drawing.FilePath) ? "" : " (missing file)";
                return $"{Drawing.Name} - {type}{status}";
            }
        }
    }
}
