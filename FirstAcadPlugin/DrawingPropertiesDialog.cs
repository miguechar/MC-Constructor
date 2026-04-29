using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FirstAcadPlugin
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

            var stack = new StackPanel { Margin = new Thickness(20) };

            stack.Children.Add(MakeLabel("Drawing Type:"));
            typeCombo = new ComboBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var type in DrawingTypes.All)
            {
                typeCombo.Items.Add(new ComboItem(type, DrawingTypes.DisplayName(type)));
            }
            typeCombo.SelectionChanged += (s, e) => UpdateDisciplineEnabled();
            stack.Children.Add(typeCombo);

            stack.Children.Add(MakeLabel("Discipline:"));
            disciplineCombo = new ComboBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12)
            };
            disciplineCombo.Items.Add(new ComboItem("", "(none)"));
            foreach (var d in DrawingDisciplines.All)
            {
                disciplineCombo.Items.Add(new ComboItem(d, d));
            }
            stack.Children.Add(disciplineCombo);

            stack.Children.Add(MakeLabel("Description:"));
            descriptionBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12),
                Height = 80,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            stack.Children.Add(descriptionBox);

            // Show the linked project for context (read-only).
            projectInfoText = new TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.Gray,
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
                Padding = new Thickness(20, 6, 20, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            saveBtn.Click += SaveButton_Click;
            buttonPanel.Children.Add(saveBtn);

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(20, 6, 20, 6),
                IsCancel = true
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

        private TextBlock MakeLabel(string text) => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
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
