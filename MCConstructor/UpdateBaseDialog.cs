using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCConstructor
{
    /// <summary>
    /// Lets the user pick a base drawing, scans all project drawings for
    /// references to it (via the MC_BaseDrawingPath SummaryInfo property),
    /// and reloads the overlay XRef in any documents that are currently open.
    /// </summary>
    public class UpdateBaseDialog : Window
    {
        private ComboBox    _baseCombo;
        private Button      _scanBtn;
        private Button      _reloadBtn;
        private ListBox     _resultList;
        private TextBlock   _statusText;

        private readonly List<Drawing> _baseDrawings;
        private readonly List<Drawing> _allDrawings;

        // Drawings found to reference the selected base.
        private List<DrawingRef> _found = new List<DrawingRef>();

        public UpdateBaseDialog(List<Drawing> baseDrawings, List<Drawing> allDrawings)
        {
            _baseDrawings = baseDrawings ?? throw new ArgumentNullException(nameof(baseDrawings));
            _allDrawings  = allDrawings  ?? throw new ArgumentNullException(nameof(allDrawings));

            Title  = "Update Base Drawing References - MC Constructor";
            Width  = 620;
            Height = 520;
            MinWidth  = 500;
            MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Background = D(45, 45, 48);

            Content = Build();
        }

        // ── UI ────────────────────────────────────────────────────────────────

        private UIElement Build()
        {
            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(R(GridUnitType.Auto));  // 0 header
            root.RowDefinitions.Add(R(GridUnitType.Auto));  // 1 base label
            root.RowDefinitions.Add(R(GridUnitType.Auto));  // 2 base combo + scan btn
            root.RowDefinitions.Add(R(GridUnitType.Auto));  // 3 results label
            root.RowDefinitions.Add(R(GridUnitType.Star));  // 4 results list
            root.RowDefinitions.Add(R(GridUnitType.Auto));  // 5 status
            root.RowDefinitions.Add(R(GridUnitType.Auto));  // 6 buttons

            // Header
            Set(root, new TextBlock
            {
                Text = "Propagate base drawing updates across the project.",
                FontSize = 12,
                Foreground = D(160, 160, 165),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            }, 0);

            // Base label
            Set(root, Label("Select Base Drawing:"), 1);

            // Base combo + Scan button on one row
            var row2 = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _baseCombo = new ComboBox
            {
                FontSize = 12,
                Padding  = new Thickness(6, 3, 6, 3),
                Background  = D(37, 37, 38),
                Foreground  = Brushes.Black,
                BorderBrush = D(67, 67, 70),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0)
            };
            foreach (var b in _baseDrawings)
                _baseCombo.Items.Add(new BaseItem(b));
            _baseCombo.SelectedIndex = 0;
            Grid.SetColumn(_baseCombo, 0);
            row2.Children.Add(_baseCombo);

            _scanBtn = Btn("Scan", D(0, 122, 204), ScanButton_Click, isDefault: false);
            Grid.SetColumn(_scanBtn, 1);
            row2.Children.Add(_scanBtn);

            Set(root, row2, 2);

            // Results label
            Set(root, Label("Drawings referencing this base:"), 3);

            // Results list
            _resultList = new ListBox
            {
                FontSize = 12,
                Background  = D(37, 37, 38),
                Foreground  = Brushes.White,
                BorderBrush = D(67, 67, 70),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Set(root, _resultList, 4);

            // Status
            _statusText = new TextBlock
            {
                FontSize = 11,
                Foreground  = D(160, 160, 165),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Text = "Click Scan to find drawings that reference the selected base."
            };
            Set(root, _statusText, 5);

            // Footer buttons
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            _reloadBtn = Btn("Reload Open Drawings", D(0, 122, 204), ReloadButton_Click, isDefault: true);
            _reloadBtn.IsEnabled = false;

            var reloadStyle = new Style(typeof(Button));
            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35));
            reloadStyle.Triggers.Add(disabledTrigger);
            _reloadBtn.Style = reloadStyle;

            footer.Children.Add(_reloadBtn);
            footer.Children.Add(Btn("Close", D(70, 70, 75), (s, e) => Close(), isDefault: false));

            Set(root, footer, 6);

            return root;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            var item = _baseCombo.SelectedItem as BaseItem;
            if (item == null) return;

            string basePath = item.Drawing.FilePath;
            _resultList.Items.Clear();
            _found.Clear();
            _reloadBtn.IsEnabled = false;
            SetStatus("Scanning…", D(160, 160, 165));

            // Scan every non-base project drawing for MC_BaseDrawingPath.
            var candidates = _allDrawings
                .Where(d => d.DrawingType != DrawingTypes.Base && File.Exists(d.FilePath))
                .ToList();

            foreach (var drawing in candidates)
            {
                try
                {
                    string storedBase = ReadBaseDrawingPath(drawing.FilePath);
                    if (!string.Equals(storedBase, basePath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isOpen = IsOpenInSession(drawing.FilePath);
                    _found.Add(new DrawingRef(drawing, isOpen));
                }
                catch { /* skip unreadable drawings */ }
            }

            if (_found.Count == 0)
            {
                SetStatus($"No drawings reference \"{item.Drawing.Name}\".", D(160, 160, 165));
                return;
            }

            foreach (var dr in _found)
                _resultList.Items.Add(new ListBoxItem
                {
                    Content    = FormatRef(dr),
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent
                });

            int openCount = _found.Count(r => r.IsOpen);
            SetStatus(
                $"Found {_found.Count} referencing drawing(s). {openCount} currently open in AutoCAD.",
                D(0, 180, 80));

            _reloadBtn.IsEnabled = openCount > 0;
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            var item = _baseCombo.SelectedItem as BaseItem;
            if (item == null) return;

            string basePath = item.Drawing.FilePath;
            int reloaded = 0;
            int errors   = 0;

            foreach (Document doc in AcadApp.DocumentManager)
            {
                if (!IsTargetDoc(doc, basePath)) continue;

                try
                {
                    using (doc.LockDocument())
                    {
                        var toReload = new ObjectIdCollection();

                        using (var tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            var bt = tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
                            foreach (ObjectId btrId in bt)
                            {
                                var btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
                                if (!btr.IsFromExternalReference) continue;

                                string resolved = ResolveXrefPath(btr.PathName, doc.Name);
                                if (string.Equals(resolved, basePath, StringComparison.OrdinalIgnoreCase))
                                    toReload.Add(btrId);
                            }
                            tr.Commit();
                        }

                        if (toReload.Count > 0)
                        {
                            doc.Database.ReloadXrefs(toReload);
                            reloaded++;
                        }
                    }
                }
                catch { errors++; }
            }

            // Refresh the list to show updated open/closed statuses.
            ScanButton_Click(sender, e);

            string msg = $"Reloaded XRef in {reloaded} open document(s).";
            if (errors > 0) msg += $" {errors} error(s) — check the AutoCAD command line.";
            SetStatus(msg, reloaded > 0 ? D(0, 180, 80) : D(220, 150, 0));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Reads only the MC_BaseDrawingPath property from a .dwg SummaryInfo
        /// using a side-database, without touching the active document.
        /// </summary>
        private static string ReadBaseDrawingPath(string filePath)
        {
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(filePath, FileShare.Read, true, "");
                db.CloseInput(true);
                return DrawingPropertiesManager.Read(db).BaseDrawingPath;
            }
        }

        private static bool IsOpenInSession(string filePath)
        {
            foreach (Document doc in AcadApp.DocumentManager)
            {
                if (string.Equals(doc.Name, filePath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>True if <paramref name="doc"/> has an XRef to <paramref name="basePath"/>.</summary>
        private static bool IsTargetDoc(Document doc, string basePath)
        {
            try
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var bt = tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
                    foreach (ObjectId btrId in bt)
                    {
                        var btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
                        if (!btr.IsFromExternalReference) continue;
                        string resolved = ResolveXrefPath(btr.PathName, doc.Name);
                        if (string.Equals(resolved, basePath, StringComparison.OrdinalIgnoreCase))
                        { tr.Commit(); return true; }
                    }
                    tr.Commit();
                }
            }
            catch { }
            return false;
        }

        private static string ResolveXrefPath(string storedPath, string hostDrawingPath)
        {
            if (string.IsNullOrEmpty(storedPath)) return null;
            if (Path.IsPathRooted(storedPath)) return storedPath;
            try
            {
                string dir = Path.GetDirectoryName(hostDrawingPath) ?? "";
                return Path.GetFullPath(Path.Combine(dir, storedPath));
            }
            catch { return storedPath; }
        }

        private static string FormatRef(DrawingRef dr)
        {
            string status = dr.IsOpen ? "[open in AutoCAD]" : "[closed]";
            return $"{dr.Drawing.Name}  —  {status}";
        }

        private void SetStatus(string msg, SolidColorBrush color)
        {
            _statusText.Text       = msg;
            _statusText.Foreground = color;
        }

        // ── Factory helpers ───────────────────────────────────────────────────

        private static SolidColorBrush D(byte r, byte g, byte b)
            => new SolidColorBrush(Color.FromRgb(r, g, b));

        private static RowDefinition R(GridUnitType t, double v = 1)
            => new RowDefinition { Height = new GridLength(v, t) };

        private static void Set(Grid g, UIElement el, int row)
        { Grid.SetRow(el, row); g.Children.Add(el); }

        private static TextBlock Label(string text) => new TextBlock
        {
            Text = text,
            FontWeight  = FontWeights.SemiBold,
            FontSize    = 12,
            Foreground  = D(200, 200, 200),
            Margin      = new Thickness(0, 0, 0, 4)
        };

        private static Button Btn(string text, SolidColorBrush bg, RoutedEventHandler click, bool isDefault)
        {
            var b = new Button
            {
                Content = text,
                Height  = 28, Padding = new Thickness(12, 0, 12, 0),
                Margin  = new Thickness(6, 0, 0, 0),
                Background = bg, Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = isDefault ? FontWeights.SemiBold : FontWeights.Normal,
                IsDefault  = isDefault,
            };
            b.Click += click;
            return b;
        }

        // ── View models ───────────────────────────────────────────────────────

        private class BaseItem
        {
            public readonly Drawing Drawing;
            public BaseItem(Drawing d) { Drawing = d; }
            public override string ToString() => Drawing.Name;
        }

        private class DrawingRef
        {
            public readonly Drawing Drawing;
            public readonly bool    IsOpen;
            public DrawingRef(Drawing d, bool open) { Drawing = d; IsOpen = open; }
        }
    }
}
