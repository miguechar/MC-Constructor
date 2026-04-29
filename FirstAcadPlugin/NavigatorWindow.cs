using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using DrawingBitmap = System.Drawing.Bitmap;
// System.Windows.Controls.Image vs Autodesk.AutoCAD.DatabaseServices.Image clash;
// alias the WPF one so the rest of the file stays readable.
using WpfImage = System.Windows.Controls.Image;
// Window has an inherited Visibility property that shadows the enum
// identifier inside the class; alias the enum so the code compiles cleanly.
using Vis = System.Windows.Visibility;

namespace FirstAcadPlugin
{
    /// <summary>
    /// MC Navigator: a project-files window that lists every drawing
    /// registered for the current project, grouped by drawing type
    /// (Functional vs Detail vs the rest), shows a thumbnail preview when
    /// a row is selected, and has an Open button to load the drawing in
    /// AutoCAD.
    /// </summary>
    public class NavigatorWindow : Window
    {
        private TreeView tree;
        private WpfImage previewImage;
        private TextBlock previewMissingText;
        private TextBlock metaText;
        private Button openButton;
        private TextBlock statusText;

        private readonly Project _project;
        private Drawing _selectedDrawing;

        /// <summary>
        /// Set when the user clicks Open (or double-clicks a drawing). The
        /// caller should run AutoCAD's Document.Open on this path AFTER the
        /// window has closed - calling it from inside the click handler
        /// throws "Invalid execution context" because the command engine
        /// will not let you open a document while a modal dialog is up.
        /// </summary>
        public string RequestedOpenPath { get; private set; }

        public NavigatorWindow(Project project)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));

            Title = $"MC Navigator - {project.Name}";
            Width = 900;
            Height = 600;
            MinWidth = 700;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            BuildUI();
            LoadDrawings();
        }

        private void BuildUI()
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var header = new TextBlock
            {
                Text = $"Project: {_project.Name}\nRoot: {_project.Directory}",
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Body: tree on the left, preview on the right
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            tree = new TreeView { FontSize = 12 };
            tree.SelectedItemChanged += Tree_SelectedItemChanged;
            Grid.SetColumn(tree, 0);
            body.Children.Add(tree);

            var splitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent
            };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);

            // Right pane: preview + metadata + status
            var rightStack = new DockPanel { LastChildFill = true };

            previewMissingText = new TextBlock
            {
                Text = "Select a drawing to preview.",
                FontSize = 12,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 8, 8, 8),
                Visibility = Vis.Visible
            };

            previewImage = new WpfImage
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(8),
                Visibility = Vis.Collapsed
            };

            // Preview area: image + missing-text overlay sit in a Border so
            // the thumbnail has a visible frame.
            var previewBorder = new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(248, 248, 248)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var previewGrid = new Grid();
            previewGrid.Children.Add(previewImage);
            previewGrid.Children.Add(previewMissingText);
            previewBorder.Child = previewGrid;
            DockPanel.SetDock(previewBorder, Dock.Top);

            metaText = new TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(metaText, Dock.Top);

            // Make preview frame fill remaining space.
            previewBorder.Height = double.NaN;
            previewBorder.MinHeight = 220;

            rightStack.Children.Add(previewBorder);
            rightStack.Children.Add(metaText);

            Grid.SetColumn(rightStack, 2);
            body.Children.Add(rightStack);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            // Footer: status + buttons
            var footer = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8, 0, 0) };

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            openButton = new Button
            {
                Content = "Open",
                Padding = new Thickness(20, 6, 20, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true,
                IsEnabled = false,
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            openButton.Click += OpenButton_Click;
            btnPanel.Children.Add(openButton);

            var refreshBtn = new Button
            {
                Content = "Refresh",
                Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            refreshBtn.Click += (s, e) => LoadDrawings();
            btnPanel.Children.Add(refreshBtn);

            var closeBtn = new Button
            {
                Content = "Close",
                Padding = new Thickness(16, 6, 16, 6),
                IsCancel = true
            };
            closeBtn.Click += (s, e) => Close();
            btnPanel.Children.Add(closeBtn);

            DockPanel.SetDock(btnPanel, Dock.Right);
            footer.Children.Add(btnPanel);

            statusText = new TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            footer.Children.Add(statusText);

            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        /// <summary>
        /// Pull all drawings for the current project, drop the ones whose
        /// .dwg file no longer exists on disk, and group them in the tree.
        /// </summary>
        private void LoadDrawings()
        {
            tree.Items.Clear();
            ClearPreview();
            statusText.Text = "Loading...";

            List<Drawing> all;
            try
            {
                all = DatabaseService.GetDrawings();
            }
            catch (System.Exception ex)
            {
                statusText.Text = $"Error loading drawings: {ex.Message}";
                statusText.Foreground = Brushes.Red;
                return;
            }

            // Only show drawings whose file lives on disk under this project.
            // The user explicitly asked "only include the project files
            // within the directory".
            string projectRoot = _project.Directory ?? "";
            var existing = all
                .Where(d => !string.IsNullOrEmpty(d.FilePath) && File.Exists(d.FilePath))
                .Where(d => string.IsNullOrEmpty(projectRoot) || IsUnder(d.FilePath, projectRoot))
                .ToList();

            // Group: Functional Drawings (with discipline subgroups), Detail
            // Drawings, then a "Standards" / "Sheets" / "Other" rollup so the
            // user has a complete picture but the user-requested division
            // between function and detail stays prominent.
            AddGroup("Functional Drawings", existing
                .Where(d => d.DrawingType == DrawingTypes.FunctionalDrawing)
                .ToList(), groupByDiscipline: true);

            AddGroup("Detail Drawings", existing
                .Where(d => d.DrawingType == DrawingTypes.DetailDrawing)
                .ToList());

            AddGroup("Block Library", existing
                .Where(d => d.DrawingType == DrawingTypes.BlockLibrary)
                .ToList());

            AddGroup("Templates", existing
                .Where(d => d.DrawingType == DrawingTypes.Template)
                .ToList());

            AddGroup("Titleblocks", existing
                .Where(d => d.DrawingType == DrawingTypes.Titleblock)
                .ToList());

            AddGroup("Sheets", existing
                .Where(d => d.DrawingType == DrawingTypes.Sheet)
                .ToList());

            AddGroup("Other", existing
                .Where(d => d.DrawingType == DrawingTypes.Other)
                .ToList());

            int hidden = all.Count - existing.Count;
            if (existing.Count == 0)
            {
                statusText.Text = "No drawings found in this project.";
                statusText.Foreground = Brushes.Gray;
            }
            else
            {
                statusText.Text = hidden > 0
                    ? $"{existing.Count} drawing(s); {hidden} skipped (missing on disk)."
                    : $"{existing.Count} drawing(s).";
                statusText.Foreground = Brushes.Gray;
            }
        }

        private void AddGroup(string title, List<Drawing> drawings, bool groupByDiscipline = false)
        {
            // Always show the group header, even when empty - that way the
            // user can see at a glance "Functional: 0".
            var headerText = $"{title} ({drawings.Count})";
            var groupItem = new TreeViewItem
            {
                Header = headerText,
                FontWeight = FontWeights.Bold,
                IsExpanded = drawings.Count > 0 && drawings.Count <= 20
            };

            if (groupByDiscipline)
            {
                foreach (var d in DrawingDisciplines.All)
                {
                    var subset = drawings
                        .Where(x => string.Equals(x.Discipline, d, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var disc = new TreeViewItem
                    {
                        Header = $"{d} ({subset.Count})",
                        FontWeight = FontWeights.Normal,
                        IsExpanded = subset.Count > 0 && subset.Count <= 12
                    };
                    foreach (var dr in subset) disc.Items.Add(MakeDrawingItem(dr));
                    groupItem.Items.Add(disc);
                }

                // Functional drawings whose discipline is missing or unknown.
                var orphans = drawings
                    .Where(x => string.IsNullOrEmpty(x.Discipline) ||
                                !DrawingDisciplines.All.Contains(x.Discipline, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (orphans.Count > 0)
                {
                    var orphanGroup = new TreeViewItem
                    {
                        Header = $"(no discipline) ({orphans.Count})",
                        FontWeight = FontWeights.Normal,
                        IsExpanded = false
                    };
                    foreach (var dr in orphans) orphanGroup.Items.Add(MakeDrawingItem(dr));
                    groupItem.Items.Add(orphanGroup);
                }
            }
            else
            {
                foreach (var dr in drawings.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                    groupItem.Items.Add(MakeDrawingItem(dr));
            }

            tree.Items.Add(groupItem);
        }

        private TreeViewItem MakeDrawingItem(Drawing d)
        {
            var item = new TreeViewItem
            {
                Header = d.Name,
                Tag = d,
                FontWeight = FontWeights.Normal
            };
            // Double-click anywhere on the row opens the drawing.
            item.MouseDoubleClick += (s, e) =>
            {
                if (item.IsSelected)
                {
                    OpenButton_Click(s, null);
                    e.Handled = true;
                }
            };
            return item;
        }

        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = e.NewValue as TreeViewItem;
            var drawing = item?.Tag as Drawing;
            _selectedDrawing = drawing;
            openButton.IsEnabled = drawing != null;

            if (drawing == null)
            {
                ClearPreview();
                return;
            }

            ShowPreview(drawing);
        }

        private void ClearPreview()
        {
            previewImage.Source = null;
            previewImage.Visibility = Vis.Collapsed;
            previewMissingText.Text = "Select a drawing to preview.";
            previewMissingText.Visibility = Vis.Visible;
            metaText.Text = "";
        }

        private void ShowPreview(Drawing drawing)
        {
            // Metadata first - cheap and always available.
            try
            {
                var fi = new FileInfo(drawing.FilePath);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Name:       {drawing.Name}");
                sb.AppendLine($"Type:       {DrawingTypes.DisplayName(drawing.DrawingType)}");
                if (!string.IsNullOrEmpty(drawing.Discipline))
                    sb.AppendLine($"Discipline: {drawing.Discipline}");
                if (!string.IsNullOrEmpty(drawing.Description))
                    sb.AppendLine($"Notes:      {drawing.Description}");
                sb.AppendLine($"Path:       {drawing.FilePath}");
                if (fi.Exists)
                {
                    sb.AppendLine($"Modified:   {fi.LastWriteTime}");
                    sb.AppendLine($"Size:       {FormatSize(fi.Length)}");
                }
                metaText.Text = sb.ToString();
            }
            catch
            {
                metaText.Text = $"Path: {drawing.FilePath}";
            }

            // Try to extract a thumbnail from the .dwg.
            BitmapSource thumb = null;
            try
            {
                thumb = TryReadThumbnail(drawing.FilePath);
            }
            catch
            {
                // Swallow - we'll just say "no preview".
            }

            if (thumb != null)
            {
                previewImage.Source = thumb;
                previewImage.Visibility = Vis.Visible;
                previewMissingText.Visibility = Vis.Collapsed;
            }
            else
            {
                previewImage.Source = null;
                previewImage.Visibility = Vis.Collapsed;
                previewMissingText.Text =
                    "No thumbnail saved in this drawing.\n\n" +
                    "Open the drawing, save it once, and the preview will be available here.";
                previewMissingText.Visibility = Vis.Visible;
            }
        }

        /// <summary>
        /// Reads the .dwg's saved thumbnail bitmap into a WPF BitmapSource.
        /// Returns null if the file cannot be read or has no thumbnail.
        /// </summary>
        private static BitmapSource TryReadThumbnail(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            using (var sideDb = new Database(false, true))
            {
                try
                {
                    sideDb.ReadDwgFile(filePath, FileShare.Read, true, "");
                    sideDb.CloseInput(true);
                }
                catch
                {
                    return null;
                }

                DrawingBitmap bmp = null;
                try
                {
                    bmp = sideDb.ThumbnailBitmap;
                }
                catch { }
                if (bmp == null) return null;

                return BitmapToSource(bmp);
            }
        }

        private static BitmapSource BitmapToSource(DrawingBitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        private static bool IsUnder(string filePath, string root)
        {
            try
            {
                var f = Path.GetFullPath(filePath);
                var r = Path.GetFullPath(root);
                if (!r.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    r += Path.DirectorySeparatorChar;
                return f.StartsWith(r, StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDrawing == null) return;
            string path = _selectedDrawing.FilePath;

            if (!File.Exists(path))
            {
                MessageBox.Show(this,
                    $"File no longer exists on disk:\n{path}",
                    "Cannot open",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Don't call DocumentManager.Open here - the command engine
            // refuses ("invalid execution context") while a modal dialog is
            // visible. Just record the request and close; the command that
            // launched the navigator will do the actual open.
            RequestedOpenPath = path;
            DialogResult = true;
            Close();
        }
    }
}
