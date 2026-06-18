using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// System.Windows.Forms is referenced for FolderBrowserDialog. Aliased to keep
// it from clashing with the WPF types in the rest of this file.
using WinForms = System.Windows.Forms;

namespace MCConstructor
{
    /// <summary>
    /// Dialog used by MCCreateProject. Collects the project name,
    /// description, and the parent directory the project root will be
    /// created in. The OK handler returns; the caller is responsible for
    /// inserting the row in the database and creating the folder layout.
    /// </summary>
    public class CreateProjectDialog : Window
    {
        private TextBox nameBox;
        private TextBox descriptionBox;
        private TextBox parentDirBox;
        private TextBlock previewText;
        private TextBlock statusText;

        /// <summary>Project name entered by the user.</summary>
        public string ProjectName { get; private set; }
        /// <summary>Optional description.</summary>
        public string Description { get; private set; }
        /// <summary>Absolute path to the project root that should be created (parent + name).</summary>
        public string ProjectRoot { get; private set; }

        public CreateProjectDialog(string defaultParentDir = null)
        {
            Title = "Create Project - MC Constructor";
            Width = 540;
            Height = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 460;
            MinHeight = 420;
            Background = D(45, 45, 48);
            DialogTheme.Apply(this);

            var stack = new StackPanel { Margin = new Thickness(20) };

            stack.Children.Add(MakeLabel("Project Name:"));
            nameBox = MakeTextBox("");
            nameBox.TextChanged += (s, e) => UpdatePreview();
            stack.Children.Add(nameBox);

            stack.Children.Add(MakeLabel("Description (optional):"));
            descriptionBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 12),
                Height = 60,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = D(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
            };
            stack.Children.Add(descriptionBox);

            stack.Children.Add(MakeLabel("Parent Directory:"));
            var browseRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };

            var browseBtn = new Button
            {
                Content = "Browse...",
                Height = 28, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Background = D(70, 70, 75), Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            browseBtn.Click += BrowseButton_Click;
            DockPanel.SetDock(browseBtn, Dock.Right);
            browseRow.Children.Add(browseBtn);

            parentDirBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                Text = defaultParentDir ?? "",
                Background = D(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
            };
            parentDirBox.TextChanged += (s, e) => UpdatePreview();
            browseRow.Children.Add(parentDirBox);

            stack.Children.Add(browseRow);

            previewText = new TextBlock
            {
                FontSize = 11,
                Foreground = D(140, 140, 145),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(previewText);

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
                Content = "Create Project",
                Height = 28, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                IsDefault = true,
                Background = D(0, 122, 204), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold,
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

            UpdatePreview();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog
            {
                Description = "Choose the parent directory for the new project",
                ShowNewFolderButton = true,
                SelectedPath = string.IsNullOrWhiteSpace(parentDirBox.Text)
                                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                                : parentDirBox.Text
            })
            {
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                {
                    parentDirBox.Text = dlg.SelectedPath;
                    UpdatePreview();
                }
            }
        }

        private void UpdatePreview()
        {
            string parent = parentDirBox.Text?.Trim() ?? "";
            string name   = nameBox.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                previewText.Text = "Project root will be created at: <fill in name + parent directory>";
                return;
            }

            try
            {
                var root = Path.Combine(parent, name);
                previewText.Text = $"Project root will be created at:\n{root}";
            }
            catch (System.Exception ex)
            {
                previewText.Text = $"Path error: {ex.Message}";
            }
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            string name   = nameBox.Text?.Trim() ?? "";
            string parent = parentDirBox.Text?.Trim() ?? "";
            string desc   = descriptionBox.Text?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                ShowError("Project name is required.");
                return;
            }
            if (ContainsInvalidPathChars(name))
            {
                ShowError("Project name contains characters that are not allowed in a folder name.");
                return;
            }
            if (string.IsNullOrEmpty(parent))
            {
                ShowError("Parent directory is required. Click Browse to pick one.");
                return;
            }
            if (!Directory.Exists(parent))
            {
                ShowError($"Parent directory does not exist:\n{parent}");
                return;
            }

            string root;
            try
            {
                root = Path.GetFullPath(Path.Combine(parent, name));
            }
            catch (System.Exception ex)
            {
                ShowError($"Invalid path: {ex.Message}");
                return;
            }

            if (Directory.Exists(root))
            {
                var resp = MessageBox.Show(
                    $"The folder already exists:\n{root}\n\nUse it anyway? Existing files will be left alone, missing subfolders will be created.",
                    "Folder exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (resp != MessageBoxResult.Yes) return;
            }

            ProjectName = name;
            Description = string.IsNullOrEmpty(desc) ? null : desc;
            ProjectRoot = root;
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

        private TextBlock MakeLabel(string text) => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Margin = new Thickness(0, 0, 0, 4)
        };

        private TextBox MakeTextBox(string defaultText) => new TextBox
        {
            Text = defaultText,
            FontSize = 12,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 12),
            Background = D(37, 37, 38), Foreground = Brushes.White,
            BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
        };
    }
}
