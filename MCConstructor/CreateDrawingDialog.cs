using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MCConstructor
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
        private ComboBox baseCombo;
        private TextBlock baseLabel;
        private TextBox nameBox;
        private TextBox descriptionBox;
        private TextBlock pathPreview;
        private TextBlock statusText;

        private readonly Project _project;
        private readonly bool _showTemplateSelector;

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
        /// <summary>
        /// Absolute path to the Base drawing to copy as the starting content
        /// for this new drawing. When set, takes precedence over TemplatePath.
        /// Null when no base was chosen.
        /// </summary>
        public string BasePath { get; private set; }

        public CreateDrawingDialog(Project project)
            : this(project, "Create Drawing - MC Constructor", "Create", null, true)
        {
        }

        public CreateDrawingDialog(
            Project project,
            string title,
            string acceptButtonText,
            string initialDrawingName,
            bool showTemplateSelector)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _showTemplateSelector = showTemplateSelector;

            Title = title;
            Width = 520;
            Height = 580;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 460;
            MinHeight = 540;
            Background = D(45, 45, 48);

            var stack = new StackPanel { Margin = new Thickness(20) };

            stack.Children.Add(MakeProjectInfoBlock(project));

            stack.Children.Add(MakeLabel("Drawing Type:"));
            typeCombo = DarkCombo(12);
            foreach (var type in DrawingTypes.All)
                typeCombo.Items.Add(new ComboItem(type, DrawingTypes.DisplayName(type)));
            typeCombo.SelectionChanged += (s, e) => { UpdateDisciplineEnabled(); UpdatePathPreview(); };
            stack.Children.Add(typeCombo);

            stack.Children.Add(MakeLabel("Discipline:"));
            disciplineCombo = DarkCombo(12);
            disciplineCombo.Items.Add(new ComboItem("", "(none)"));
            foreach (var d in DrawingDisciplines.All)
                disciplineCombo.Items.Add(new ComboItem(d, d));
            disciplineCombo.SelectionChanged += (s, e) => UpdatePathPreview();
            stack.Children.Add(disciplineCombo);

            var templateLabel = MakeLabel("Template:");
            templateLabel.Visibility = showTemplateSelector ? Visibility.Visible : Visibility.Collapsed;
            stack.Children.Add(templateLabel);
            templateCombo = DarkCombo(12);
            templateCombo.Visibility = showTemplateSelector ? Visibility.Visible : Visibility.Collapsed;
            PopulateTemplateCombo();
            stack.Children.Add(templateCombo);

            baseLabel = MakeLabel("Choose Base (optional):");
            stack.Children.Add(baseLabel);
            baseCombo = DarkCombo(12);
            PopulateBaseCombo();
            stack.Children.Add(baseCombo);

            stack.Children.Add(MakeLabel("Drawing Name (no extension):"));
            nameBox = DarkBox(12);
            nameBox.Text = initialDrawingName ?? "";
            nameBox.TextChanged += (s, e) => UpdatePathPreview();
            stack.Children.Add(nameBox);

            stack.Children.Add(MakeLabel("Description (optional):"));
            descriptionBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 12),
                Height = 50,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = D(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
            };
            stack.Children.Add(descriptionBox);

            pathPreview = new TextBlock
            {
                FontSize = 11,
                Foreground = D(140, 140, 145),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(pathPreview);

            statusText = new TextBlock
            {
                FontSize = 11,
                Foreground = D(140, 140, 145),
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
                Content = acceptButtonText,
                Height = 28, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                IsDefault = true,
                Background = D(0, 122, 204), Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
            };
            createBtn.Click += CreateButton_Click;
            buttonPanel.Children.Add(createBtn);

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
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 145)),
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

            // Base drawings cannot themselves be based on another base.
            bool isBase = t?.Value == DrawingTypes.Base;
            bool showBaseSelector = _showTemplateSelector && !isBase;
            baseLabel.Visibility = showBaseSelector ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            baseCombo.Visibility  = showBaseSelector ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (!showBaseSelector)
                baseCombo.SelectedIndex = 0;
        }

        /// <summary>
        /// Fill the Base combo from <projectRoot>\01 Standards\Bases.
        /// First item is always "(none)" so the field is optional.
        /// </summary>
        private void PopulateBaseCombo()
        {
            baseCombo.Items.Clear();
            baseCombo.Items.Add(new ComboItem("", "(none - no base)"));

            try
            {
                if (!string.IsNullOrEmpty(_project.Directory))
                {
                    string basesDir = Path.Combine(_project.Directory, ProjectFolderLayout.Bases);
                    if (Directory.Exists(basesDir))
                    {
                        var files = Directory
                            .EnumerateFiles(basesDir, "*.dwg", SearchOption.TopDirectoryOnly)
                            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);

                        foreach (var path in files)
                            baseCombo.Items.Add(new ComboItem(path, Path.GetFileNameWithoutExtension(path)));
                    }
                }
            }
            catch
            {
                // Non-fatal: just won't list bases.
            }

            baseCombo.SelectedIndex = 0;
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

            var tpl = _showTemplateSelector ? templateCombo.SelectedItem as ComboItem : null;
            TemplatePath = string.IsNullOrEmpty(tpl?.Value) ? null : tpl.Value;

            var baseItem = baseCombo.SelectedItem as ComboItem;
            BasePath = string.IsNullOrEmpty(baseItem?.Value) ? null : baseItem.Value;

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
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 70, 70));
        }

        private static SolidColorBrush D(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

        private static ComboBox DarkCombo(int fontSize) => new ComboBox
        {
            FontSize = fontSize, Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 12),
            Background = D(37, 37, 38), Foreground = Brushes.Black,
            BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
        };

        private static TextBox DarkBox(int fontSize) => new TextBox
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
