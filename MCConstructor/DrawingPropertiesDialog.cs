using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MCConstructor
{
    /// <summary>
    /// Dialog for viewing and editing the MC Constructor drawing properties
    /// (drawing type, discipline, description).
    /// </summary>
    public class DrawingPropertiesDialog : Window
    {
        private ComboBox typeCombo;
        private ComboBox disciplineCombo;
        private TextBox descriptionBox;
        private TextBlock projectInfoText;

        public DrawingPropertiesData Result { get; private set; }

        public DrawingPropertiesDialog(DrawingPropertiesData initial)
        {
            Title = "Drawing Properties - MC Constructor";
            Width = 460;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = D(45, 45, 48);

            var stack = new StackPanel { Margin = new Thickness(20) };

            stack.Children.Add(MakeLabel("Drawing Type:"));
            typeCombo = DarkCombo(12);
            foreach (var type in DrawingTypes.All)
                typeCombo.Items.Add(new ComboItem(type, DrawingTypes.DisplayName(type)));
            typeCombo.SelectionChanged += (s, e) => UpdateDisciplineEnabled();
            stack.Children.Add(typeCombo);

            stack.Children.Add(MakeLabel("Discipline:"));
            disciplineCombo = DarkCombo(12);
            disciplineCombo.Items.Add(new ComboItem("", "(none)"));
            foreach (var d in DrawingDisciplines.All)
                disciplineCombo.Items.Add(new ComboItem(d, d));
            stack.Children.Add(disciplineCombo);

            stack.Children.Add(MakeLabel("Description:"));
            descriptionBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 12),
                Height = 80,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = D(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
            };
            stack.Children.Add(descriptionBox);

            projectInfoText = new TextBlock
            {
                FontSize = 11,
                Foreground = D(140, 140, 145),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(projectInfoText);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var saveBtn = new Button
            {
                Content = "Save",
                Height = 28, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                IsDefault = true,
                Background = D(0, 122, 204), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold,
            };
            saveBtn.Click += SaveButton_Click;
            buttonPanel.Children.Add(saveBtn);

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Height = 28, Padding = new Thickness(12, 0, 12, 0),
                IsCancel = true,
                Background = D(70, 70, 75), Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelBtn);

            stack.Children.Add(buttonPanel);
            Content = stack;

            Populate(initial);
        }

        private void Populate(DrawingPropertiesData data)
        {
            data = data ?? new DrawingPropertiesData();

            // Drawing type
            var typeItem = typeCombo.Items.OfType<ComboItem>()
                .FirstOrDefault(i => i.Value == data.DrawingType);
            typeCombo.SelectedItem = typeItem ?? typeCombo.Items[0];

            // Discipline
            var discItem = disciplineCombo.Items.OfType<ComboItem>()
                .FirstOrDefault(i => i.Value == (data.Discipline ?? ""));
            disciplineCombo.SelectedItem = discItem ?? disciplineCombo.Items[0];

            descriptionBox.Text = data.Description ?? "";

            string projectName = data.ProjectName;
            if (string.IsNullOrEmpty(projectName) && DatabaseService.CurrentProject != null)
                projectName = DatabaseService.CurrentProject.Name;

            projectInfoText.Text = string.IsNullOrEmpty(projectName)
                ? "Linked project: (none - drawing is not linked to a project)"
                : $"Linked project: {projectName}";

            UpdateDisciplineEnabled();
        }

        private void UpdateDisciplineEnabled()
        {
            var item = typeCombo.SelectedItem as ComboItem;
            bool requires = item != null && DrawingTypes.RequiresDiscipline(item.Value);
            disciplineCombo.IsEnabled = requires;
            if (!requires)
            {
                disciplineCombo.SelectedIndex = 0; // "(none)"
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var typeItem = typeCombo.SelectedItem as ComboItem;
            var discItem = disciplineCombo.SelectedItem as ComboItem;

            if (typeItem == null)
            {
                MessageBox.Show("Choose a drawing type.", "Missing field",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string discipline = string.IsNullOrEmpty(discItem?.Value) ? null : discItem.Value;

            // Discipline is required for functional drawings.
            if (DrawingTypes.RequiresDiscipline(typeItem.Value) && discipline == null)
            {
                MessageBox.Show("Select a discipline for a functional drawing.",
                    "Missing field", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new DrawingPropertiesData
            {
                DrawingType = typeItem.Value,
                Discipline  = discipline,
                Description = string.IsNullOrWhiteSpace(descriptionBox.Text) ? null : descriptionBox.Text.Trim()
            };
            DialogResult = true;
            Close();
        }

        private static SolidColorBrush D(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

        private static ComboBox DarkCombo(int fontSize) => new ComboBox
        {
            FontSize = fontSize, Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 12),
            Background = D(37, 37, 38), Foreground = Brushes.White,
            BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
        };

        private TextBlock MakeLabel(string text) => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Margin = new Thickness(0, 0, 0, 4)
        };

        private class ComboItem
        {
            public string Value { get; }
            public string Display { get; }
            public ComboItem(string value, string display) { Value = value; Display = display; }
            public override string ToString() => Display;
        }
    }
}
