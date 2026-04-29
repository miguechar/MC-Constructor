using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Dialog used by MCCreateDrawing. Collects drawing type, discipline
    /// (when applicable), name, and an optional description. The OK handler
    /// returns; the caller is responsible for actually creating the .dwg
    /// file and registering it in the database.
    /// </summary>
    public class CreateDrawingDialog : Window
    {
        private ComboBox typeCombo;
        private ComboBox disciplineCombo;
        private ComboBox templateCombo;
        private TextBox nameBox;
        private TextBox descriptionBox;
        private TextBlock pathPreview;
        private TextBlock statusText;

        private readonly Project _project;

        public string DrawingType { get; private set; }
        public string Discipline { get; private set; }
        public string DrawingName { get; private set; }
        public string Description { get; private set; }
        /// <summary>Absolute path the new .dwg file will be saved to.</summary>
        public string TargetFilePath { get; private set; }
        /// <summary>
        /// Absolute path to the template (.dwt or .dwg) the new drawing should
        /// be seeded from, or null to start from a blank default drawing.
        /// </summary>
        public string TemplatePath { get; private set; }

        public CreateDrawingDialog(Project project)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));

            Title = "Create Drawing - MC Constructor";
            Width = 520;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 460;
            MinHeight = 480;

            var stack = new StackPanel { Margin = new Thickness(20) };

            stack.Children.Add(MakeProjectInfoBlock(project));

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
            typeCombo.SelectionChanged += (s, e) => { UpdateDisciplineEnabled(); UpdatePathPreview(); };
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
                disciplineCombo.Items.Add(new ComboItem(d, d));
            disciplineCombo.SelectionChanged += (s, e) => UpdatePathPreview();
            stack.Children.Add(disciplineCombo);

            stack.Children.Add(MakeLabel("Template:"));
            templateCombo = new ComboBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12)
            };
            PopulateTemplateCombo();
            stack.Children.Add(templateCombo);

            stack.Children.Add(MakeLabel("Drawing Name (no extension):"));
            nameBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12)
            };
            nameBox.TextChanged += (s, e) => UpdatePathPreview();
            stack.Children.Add(nameBox);

            stack.Children.Add(MakeLabel("Description (optional):"));
            descriptionBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12),
                Height = 50,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            stack.Children.Add(descriptionBox);

            pathPreview = new TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(pathPreview);

            statusText = new TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(statusText);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var createBtn = new Button
            {
                Content = "Create",
                Padding = new Thickness(20, 6, 20, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            createBtn.Click += CreateButton_Click;
            buttonPanel.Children.Add(createBtn);

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

            // Default: Functional / General so most users land in 02 Models\General.
            typeCombo.SelectedItem = typeCombo.Items.OfType<ComboItem>()
                .FirstOrDefault(i => i.Value == DrawingTypes.FunctionalDrawing) ?? typeCombo.Items[0];
            disciplineCombo.SelectedItem = disciplineCombo.Items.OfType<ComboItem>()
                .FirstOrDefault(i => i.Value == DrawingDisciplines.General) ?? disciplineCombo.Items[0];

            UpdateDisciplineEnabled();
            UpdatePathPreview();
        }

        private TextBlock MakeProjectInfoBlock(Project p)
        {
            return new TextBlock
            {
                Text = $"Project: {p.Name}\nRoot:    {p.Directory}",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        /// <summary>
        /// Fill the Template combo from <projectRoot>\01 Standards\Templates.
        /// Always includes a "(blank)" first item so the user can opt out.
        /// Both .dwt and .dwg files are listed.
        /// </summary>
        private void PopulateTemplateCombo()
        {
            templateCombo.Items.Clear();
            templateCombo.Items.Add(new ComboItem("", "(blank - no template)"));

            try
            {
                if (!string.IsNullOrEmpty(_project.Directory))
                {
                    string templateDir = Path.Combine(_project.Directory, ProjectFolderLayout.Templates);
                    if (Directory.Exists(templateDir))
                    {
                        var files = Directory
                            .EnumerateFiles(templateDir, "*.dwt", SearchOption.TopDirectoryOnly)
                            .Concat(Directory.EnumerateFiles(templateDir, "*.dwg", SearchOption.TopDirectoryOnly))
                            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);

                        foreach (var path in files)
                        {
                            templateCombo.Items.Add(new ComboItem(path, Path.GetFileName(path)));
                        }
                    }
                }
            }
            catch
            {
                // Non-fatal: we just won't list templates.
            }

            templateCombo.SelectedIndex = 0;
        }

        private void UpdateDisciplineEnabled()
        {
            var t = typeCombo.SelectedItem as ComboItem;
            bool requires = t != null && DrawingTypes.RequiresDiscipline(t.Value);
            disciplineCombo.IsEnabled = requires;
            if (!requires)
                disciplineCombo.SelectedIndex = 0;
        }

        private void UpdatePathPreview()
        {
            var t = typeCombo.SelectedItem as ComboItem;
            var d = disciplineCombo.SelectedItem as ComboItem;
            string name = nameBox.Text?.Trim() ?? "";

            if (t == null)
            {
                pathPreview.Text = "Pick a drawing type to see the target path.";
                return;
            }

            string discipline = string.IsNullOrEmpty(d?.Value) ? null : d.Value;
            string folder = DrawingTypes.RelativeFolder(t.Value, discipline);

            if (string.IsNullOrEmpty(name))
            {
                pathPreview.Text = $"Will be saved to:\n{Path.Combine(_project.Directory ?? "", folder)}\\<name>.dwg";
            }
            else
            {
                string fileName = name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) ? name : name + ".dwg";
                pathPreview.Text = $"Will be saved to:\n{Path.Combine(_project.Directory ?? "", folder, fileName)}";
            }
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            var t = typeCombo.SelectedItem as ComboItem;
            var d = disciplineCombo.SelectedItem as ComboItem;
            string name = nameBox.Text?.Trim() ?? "";

            if (t == null)
            {
                ShowError("Select a drawing type.");
                return;
            }
            if (DrawingTypes.RequiresDiscipline(t.Value) && string.IsNullOrEmpty(d?.Value))
            {
                ShowError("Select a discipline for a functional drawing.");
                return;
            }
            if (string.IsNullOrEmpty(name))
            {
                ShowError("Drawing name is required.");
                return;
            }
            if (ContainsInvalidPathChars(name))
            {
                ShowError("Drawing name contains characters that are not allowed in a file name.");
                return;
            }
            if (string.IsNullOrEmpty(_project.Directory))
            {
                ShowError("Current project does not have a directory set. Open a project that was created with MCCreateProject.");
                return;
            }

            string discipline = string.IsNullOrEmpty(d?.Value) ? null : d.Value;
            string folder     = DrawingTypes.RelativeFolder(t.Value, discipline);
            string fileName   = name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) ? name : name + ".dwg";
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(_project.Directory, folder, fileName));
            }
            catch (System.Exception ex)
            {
                ShowError($"Invalid path: {ex.Message}");
                return;
            }

            if (File.Exists(fullPath))
            {
                ShowError($"A drawing with this name already exists at:\n{fullPath}");
                return;
            }

            DrawingType    = t.Value;
            Discipline     = discipline;
            DrawingName    = name;
            Description    = string.IsNullOrWhiteSpace(descriptionBox.Text) ? null : descriptionBox.Text.Trim();
            TargetFilePath = fullPath;

            var tpl = templateCombo.SelectedItem as ComboItem;
            TemplatePath = string.IsNullOrEmpty(tpl?.Value) ? null : tpl.Value;

            DialogResult = true;
            Close();
        }

        private static bool ContainsInvalidPathChars(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                if (s.IndexOf(c) >= 0) return true;
            return false;
        }

        private void ShowError(string message)
        {
            statusText.Text = message;
            statusText.Foreground = Brushes.Red;
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
