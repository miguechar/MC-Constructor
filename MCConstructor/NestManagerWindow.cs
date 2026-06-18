using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AcDb = Autodesk.AutoCAD.DatabaseServices;

namespace MCConstructor
{
    /// <summary>
    /// Shows all nest records for the current project. Supports multi-selection,
    /// DXF export ("Send to Cut"), and manual cut-date recording.
    /// </summary>
    public class NestManagerWindow : Window
    {
        // ── controls ─────────────────────────────────────────────────────────
        private DataGrid   _grid;
        private Image      _previewImage;
        private TextBlock  _previewInfo;
        private Button     _sendToCutBtn;
        private Button     _markCutBtn;
        private StackPanel _cutPanel;
        private DatePicker _cutDatePicker;
        private TextBlock  _statusBar;

        // ── data ─────────────────────────────────────────────────────────────
        private List<NestRow> _rows = new List<NestRow>();

        /// <summary>
        /// Set when the user double-clicks a nest row to open its DWG.
        /// The caller must open this path AFTER the dialog closes — AutoCAD
        /// refuses DocumentManager.Open while a modal dialog is visible.
        /// </summary>
        public string RequestedOpenPath { get; private set; }

        // ── theme (matches existing dark theme) ───────────────────────────────
        private static SolidColorBrush C(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
        private static readonly SolidColorBrush BgDark      = C(45,  45,  48);
        private static readonly SolidColorBrush BgMid       = C(63,  63,  70);
        private static readonly SolidColorBrush BgAlt       = C(37,  37,  40);
        private static readonly SolidColorBrush BgPanel     = C(30,  30,  32);
        private static readonly SolidColorBrush AccentBlue  = C(0,   102, 180);
        private static readonly SolidColorBrush AccentGreen = C(40,  160,  80);
        private static readonly SolidColorBrush TextWhite   = C(255, 255, 255);
        private static readonly SolidColorBrush TextGray    = C(160, 160, 165);

        // ─────────────────────────────────────────────────────────────────────

        public NestManagerWindow()
        {
            var project = DatabaseService.CurrentProject;
            Title = project != null
                ? $"MC Nest Manager — {project.Name}"
                : "MC Nest Manager";

            Width  = 1150;
            Height = 720;
            MinWidth  = 900;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode  = ResizeMode.CanResize;
            Background  = BgDark;
            DialogTheme.Apply(this);

            Content = BuildLayout();
            Loaded += (_, __) => LoadNests();
        }

        // ── layout ─────────────────────────────────────────────────────────────

        private UIElement BuildLayout()
        {
            // status bar
            _statusBar = new TextBlock { Foreground = TextGray, FontSize = 11, Margin = new Thickness(8, 4, 8, 4) };

            // cut-date inline panel (hidden by default)
            _cutDatePicker = new DatePicker
            {
                SelectedDate      = DateTime.Today,
                Width             = 160,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0)
            };
            var confirmCutBtn = MakeBtn("Confirm Mark Cut", AccentGreen, OnConfirmCut);
            var cancelCutBtn  = MakeBtn("Cancel",           BgMid,       (_, __) => { _cutPanel.Visibility = Visibility.Collapsed; });
            _cutPanel = new StackPanel { Orientation = Orientation.Horizontal, Background = BgPanel, Visibility = Visibility.Collapsed };
            _cutPanel.Children.Add(new TextBlock { Text = "Cut date:", Foreground = TextWhite, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
            _cutPanel.Children.Add(_cutDatePicker);
            _cutPanel.Children.Add(confirmCutBtn);
            _cutPanel.Children.Add(cancelCutBtn);

            // action button bar
            _sendToCutBtn = MakeBtn("Send to Cut",  AccentBlue,  OnSendToCut);
            _markCutBtn   = MakeBtn("Mark as Cut",  AccentGreen, OnToggleCutPanel);
            var refreshBtn = MakeBtn("Refresh",     BgMid,       (_, __) => LoadNests());
            var closeBtn   = MakeBtn("Close",       BgMid,       (_, __) => Close());
            _sendToCutBtn.IsEnabled = false;
            _markCutBtn.IsEnabled   = false;

            var leftBtns = new StackPanel { Orientation = Orientation.Horizontal };
            leftBtns.Children.Add(_sendToCutBtn);
            leftBtns.Children.Add(_markCutBtn);
            leftBtns.Children.Add(refreshBtn);

            var btnBar = new DockPanel { Background = BgPanel, LastChildFill = true };
            DockPanel.SetDock(closeBtn, Dock.Right);
            btnBar.Children.Add(closeBtn);
            btnBar.Children.Add(leftBtns);

            // build outer DockPanel — bottom items must be added before the fill child
            var statusBorder = new Border { Background = BgPanel, Child = _statusBar };
            var outer = new DockPanel { LastChildFill = true, Background = BgDark };
            DockPanel.SetDock(statusBorder, Dock.Bottom);
            DockPanel.SetDock(btnBar,       Dock.Bottom);
            DockPanel.SetDock(_cutPanel,    Dock.Bottom);
            outer.Children.Add(statusBorder);
            outer.Children.Add(btnBar);
            outer.Children.Add(_cutPanel);
            outer.Children.Add(BuildMainContent());

            return outer;
        }

        private UIElement BuildMainContent()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            var left = BuildGridPanel();
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var splitter = new GridSplitter
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = BgMid
            };
            Grid.SetColumn(splitter, 1);
            grid.Children.Add(splitter);

            var right = BuildPreviewPanel();
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            return grid;
        }

        private UIElement BuildGridPanel()
        {
            _grid = new DataGrid
            {
                AutoGenerateColumns      = false,
                SelectionMode            = DataGridSelectionMode.Extended,
                SelectionUnit            = DataGridSelectionUnit.FullRow,
                IsReadOnly               = true,
                HeadersVisibility        = DataGridHeadersVisibility.Column,
                GridLinesVisibility      = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = BgMid,
                Background               = BgDark,
                Foreground               = TextWhite,
                RowBackground            = BgDark,
                AlternatingRowBackground = BgAlt,
                BorderThickness          = new Thickness(0),
                CanUserReorderColumns    = false,
                CanUserResizeRows        = false,
                CanUserSortColumns       = true
            };

            // column header style
            var hdrStyle = new Style(typeof(DataGridColumnHeader));
            hdrStyle.Setters.Add(new Setter(Control.BackgroundProperty,      BgMid));
            hdrStyle.Setters.Add(new Setter(Control.ForegroundProperty,      TextWhite));
            hdrStyle.Setters.Add(new Setter(Control.FontWeightProperty,      FontWeights.SemiBold));
            hdrStyle.Setters.Add(new Setter(Control.PaddingProperty,         new Thickness(6, 4, 6, 4)));
            hdrStyle.Setters.Add(new Setter(Control.BorderBrushProperty,     BgDark));
            hdrStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            _grid.ColumnHeaderStyle = hdrStyle;

            // row style - highlight selection
            var rowStyle = new Style(typeof(DataGridRow));
            var selTrig = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            selTrig.Setters.Add(new Setter(DataGridRow.BackgroundProperty, AccentBlue));
            selTrig.Setters.Add(new Setter(DataGridRow.ForegroundProperty, TextWhite));
            rowStyle.Triggers.Add(selTrig);
            _grid.RowStyle = rowStyle;

            // cell style
            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(Control.PaddingProperty,         new Thickness(6, 3, 6, 3)));
            var cellSelTrig = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            cellSelTrig.Setters.Add(new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent));
            cellStyle.Triggers.Add(cellSelTrig);
            _grid.CellStyle = cellStyle;

            // columns
            _grid.Columns.Add(TxtCol("Nest",         nameof(NestRow.Name),              200, true));
            _grid.Columns.Add(TxtCol("Date Created",  nameof(NestRow.CreatedAtDisplay),  130));
            _grid.Columns.Add(TxtCol("Material",      nameof(NestRow.MaterialName),      100));
            _grid.Columns.Add(TxtCol("Plate",         nameof(NestRow.PlateDisplay),      160));
            _grid.Columns.Add(TxtCol("Parts",         nameof(NestRow.PartCount),          50));
            _grid.Columns.Add(TxtCol("Efficiency",    nameof(NestRow.EfficiencyDisplay),  80));
            _grid.Columns.Add(ChkCol("Sent to Cut",   nameof(NestRow.SentToCut)));
            _grid.Columns.Add(ChkCol("Cut",           nameof(NestRow.Cut)));
            _grid.Columns.Add(TxtCol("Cut Date",      nameof(NestRow.CutDateDisplay),    100));
            _grid.Columns.Add(TxtCol("Location",      nameof(NestRow.NestLocation),      200, true));

            _grid.SelectionChanged += OnSelectionChanged;
            _grid.MouseDoubleClick += (_, __) => OpenNestDrawing();

            return _grid;
        }

        private static DataGridTextColumn TxtCol(string header, string binding, double width, bool star = false)
        {
            var col = new DataGridTextColumn
            {
                Header  = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width   = star
                    ? new DataGridLength(1, DataGridLengthUnitType.Star)
                    : new DataGridLength(width)
            };
            col.ElementStyle = new Style(typeof(TextBlock))
            {
                Setters = { new Setter(TextBlock.ForegroundProperty, TextWhite) }
            };
            return col;
        }

        private static DataGridCheckBoxColumn ChkCol(string header, string binding)
        {
            return new DataGridCheckBoxColumn
            {
                Header     = header,
                Binding    = new System.Windows.Data.Binding(binding),
                Width      = new DataGridLength(75),
                IsReadOnly = true
            };
        }

        private UIElement BuildPreviewPanel()
        {
            _previewImage = new Image
            {
                Stretch             = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                Margin              = new Thickness(8)
            };

            var imgBorder = new Border
            {
                Background = BgPanel,
                MinHeight  = 280,
                Margin     = new Thickness(8, 8, 8, 4),
                Child      = _previewImage
            };

            _previewInfo = new TextBlock
            {
                Foreground  = TextGray,
                FontSize    = 12,
                Margin      = new Thickness(12, 4, 8, 8),
                TextWrapping = TextWrapping.Wrap,
                LineHeight  = 20
            };

            var stack = new StackPanel();
            stack.Children.Add(imgBorder);
            stack.Children.Add(_previewInfo);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = stack
            };

            SetPreviewEmpty("Select a nest to preview.");
            return new Border { Background = BgDark, Child = scroll };
        }

        private static Button MakeBtn(string text, SolidColorBrush bg, RoutedEventHandler onClick)
        {
            var btn = new Button
            {
                Content         = text,
                Background      = bg,
                Foreground      = TextWhite,
                Padding         = new Thickness(14, 7, 14, 7),
                Margin          = new Thickness(4, 6, 4, 6),
                BorderThickness = new Thickness(0),
                Cursor          = System.Windows.Input.Cursors.Hand
            };
            btn.Click += onClick;
            return btn;
        }

        // ── data loading ───────────────────────────────────────────────────────

        private void LoadNests()
        {
            _rows.Clear();
            try
            {
                var records = DatabaseService.GetNests();
                foreach (var r in records)
                    _rows.Add(new NestRow(r));
                _grid.ItemsSource = null;
                _grid.ItemsSource = _rows;
                _statusBar.Text = $"{_rows.Count} nest(s) loaded.";
            }
            catch (Exception ex)
            {
                _statusBar.Text = $"Error loading nests: {ex.Message}";
            }
            UpdateButtons();
        }

        // ── selection ──────────────────────────────────────────────────────────

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtons();
            UpdatePreview();
        }

        private void UpdateButtons()
        {
            var sel = SelectedRows();
            _sendToCutBtn.IsEnabled = sel.Any(r => !string.IsNullOrEmpty(r.Record.DwgPath));
            _markCutBtn.IsEnabled   = sel.Count > 0;
            if (sel.Count == 0)
                _cutPanel.Visibility = Visibility.Collapsed;
        }

        private void UpdatePreview()
        {
            var sel = SelectedRows();

            if (sel.Count == 0) { SetPreviewEmpty("No nest selected."); return; }
            if (sel.Count > 1)  { SetPreviewEmpty($"{sel.Count} nests selected."); return; }

            var rec = sel[0].Record;

            BitmapSource thumb = null;
            if (!string.IsNullOrEmpty(rec.DwgPath) && File.Exists(rec.DwgPath))
                thumb = LoadDwgThumbnail(rec.DwgPath);

            _previewImage.Source = thumb;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(rec.Name ?? "");
            if (!string.IsNullOrEmpty(rec.MaterialName))
                sb.AppendLine($"Material:    {rec.MaterialName}");
            if (!string.IsNullOrEmpty(rec.PlateCode) || !string.IsNullOrEmpty(rec.PlateDimensions))
                sb.AppendLine($"Plate:       {rec.PlateCode} {rec.PlateDimensions}".Trim());
            sb.AppendLine($"Parts:       {rec.PartCount}");
            sb.AppendLine($"Efficiency:  {rec.Efficiency:F1}%");
            sb.AppendLine($"Created:     {rec.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
            if (rec.SentToCut)
                sb.AppendLine("Sent to cut: yes");
            if (rec.Cut)
                sb.AppendLine($"Cut:         {(rec.CutDate.HasValue ? rec.CutDate.Value.ToString("yyyy-MM-dd") : "yes")}");
            if (!string.IsNullOrEmpty(rec.NestLocation))
                sb.AppendLine($"DXF:         {rec.NestLocation}");
            if (!string.IsNullOrEmpty(rec.DwgPath))
                sb.AppendLine($"DWG:         {rec.DwgPath}");
            if (thumb == null)
                sb.AppendLine(File.Exists(rec.DwgPath ?? "") ? "(No thumbnail embedded)" : "(DWG not on disk)");

            _previewInfo.Text = sb.ToString();
        }

        private void SetPreviewEmpty(string msg)
        {
            _previewImage.Source = null;
            _previewInfo.Text    = msg;
        }

        // ── actions ────────────────────────────────────────────────────────────

        private void OnSendToCut(object sender, RoutedEventArgs e)
        {
            var sel = SelectedRows().Where(r => !string.IsNullOrEmpty(r.Record.DwgPath)).ToList();
            if (sel.Count == 0)
            {
                MessageBox.Show("No selected nests have a DWG file to export.",
                    "Send to Cut", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description         = "Select folder to export DXF files";
                dlg.ShowNewFolderButton = true;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                string folder  = dlg.SelectedPath;
                int    exported = 0, skipped = 0;
                var    errors  = new List<string>();

                foreach (var row in sel)
                {
                    var rec = row.Record;
                    if (!File.Exists(rec.DwgPath)) { skipped++; continue; }

                    string dxfName = Path.GetFileNameWithoutExtension(rec.DwgPath) + ".dxf";
                    string dxfPath = Path.Combine(folder, dxfName);

                    try
                    {
                        ExportDwgToDxf(rec.DwgPath, dxfPath);
                        DatabaseService.UpdateNestSentToCut(rec.Id, dxfPath);
                        row.SentToCut    = true;
                        row.NestLocation = dxfPath;
                        exported++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{rec.Name}: {ex.Message}");
                    }
                }

                _grid.Items.Refresh();
                UpdatePreview();
                _statusBar.Text = $"Exported {exported} DXF file(s).";

                string msg = $"Exported {exported} DXF file(s) to:\n{folder}";
                if (skipped  > 0) msg += $"\n\nSkipped {skipped} (DWG not on disk).";
                if (errors.Count > 0) msg += "\n\nErrors:\n" + string.Join("\n", errors);

                MessageBox.Show(msg, "Send to Cut", MessageBoxButton.OK,
                    errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
        }

        private void OnToggleCutPanel(object sender, RoutedEventArgs e)
        {
            if (_cutPanel.Visibility == Visibility.Visible)
            {
                _cutPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                _cutDatePicker.SelectedDate = DateTime.Today;
                _cutPanel.Visibility        = Visibility.Visible;
            }
        }

        private void OnConfirmCut(object sender, RoutedEventArgs e)
        {
            var sel    = SelectedRows();
            if (sel.Count == 0) return;

            DateTime? cutDate = _cutDatePicker.SelectedDate;
            int       updated = 0;
            var       errors  = new List<string>();

            foreach (var row in sel)
            {
                try
                {
                    DatabaseService.UpdateNestCut(row.Record.Id, true, cutDate);
                    row.Cut    = true;
                    row.CutDate = cutDate;
                    updated++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{row.Record.Name}: {ex.Message}");
                }
            }

            _grid.Items.Refresh();
            _cutPanel.Visibility = Visibility.Collapsed;
            UpdatePreview();
            _statusBar.Text = $"Marked {updated} nest(s) as cut.";

            if (errors.Count > 0)
                MessageBox.Show("Some updates failed:\n" + string.Join("\n", errors),
                    "Mark as Cut", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OpenNestDrawing()
        {
            var sel = SelectedRows();
            if (sel.Count != 1) return;
            var rec = sel[0].Record;
            if (string.IsNullOrEmpty(rec.DwgPath) || !File.Exists(rec.DwgPath)) return;

            // Can't call DocumentManager.Open while a modal dialog is visible —
            // AutoCAD throws "invalid execution context". Store the path and close;
            // the MCNestManager command will open it after the dialog returns.
            RequestedOpenPath = rec.DwgPath;
            DialogResult = true;
            Close();
        }

        // ── helpers ─────────────────────────────────────────────────────────────

        private List<NestRow> SelectedRows()
            => _grid.SelectedItems.OfType<NestRow>().ToList();

        private static BitmapSource LoadDwgThumbnail(string dwgPath)
        {
            try
            {
                using (var db = new AcDb.Database(false, true))
                {
                    db.ReadDwgFile(dwgPath, FileShare.Read, true, "");
                    var bmp = db.ThumbnailBitmap;
                    if (bmp == null) return null;

                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Seek(0, SeekOrigin.Begin);
                        var wpfBmp = new BitmapImage();
                        wpfBmp.BeginInit();
                        wpfBmp.StreamSource = ms;
                        wpfBmp.CacheOption  = BitmapCacheOption.OnLoad;
                        wpfBmp.EndInit();
                        wpfBmp.Freeze();
                        return wpfBmp;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static void ExportDwgToDxf(string dwgPath, string dxfPath)
        {
            using (var db = new AcDb.Database(false, true))
            {
                db.ReadDwgFile(dwgPath, FileShare.Read, true, "");
                db.DxfOut(dxfPath, 16, AcDb.DwgVersion.Current);
            }
        }

        // ── view-model row ────────────────────────────────────────────────────

        public class NestRow : System.ComponentModel.INotifyPropertyChanged
        {
            public NestRecord Record { get; }

            private bool      _sentToCut;
            private bool      _cut;
            private DateTime? _cutDate;
            private string    _nestLocation;

            public NestRow(NestRecord r)
            {
                Record        = r;
                _sentToCut    = r.SentToCut;
                _cut          = r.Cut;
                _cutDate      = r.CutDate;
                _nestLocation = r.NestLocation;
            }

            public string Name             => Record.Name;
            public string CreatedAtDisplay => Record.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            public string MaterialName     => string.IsNullOrEmpty(Record.MaterialName) ? "—" : Record.MaterialName;
            public int    PartCount        => Record.PartCount;
            public string EfficiencyDisplay => $"{Record.Efficiency:F1}%";

            public string PlateDisplay
            {
                get
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(Record.PlateCode))       parts.Add(Record.PlateCode);
                    if (!string.IsNullOrEmpty(Record.PlateDimensions)) parts.Add(Record.PlateDimensions);
                    return parts.Count > 0 ? string.Join(" ", parts) : "—";
                }
            }

            public bool SentToCut
            {
                get => _sentToCut;
                set { _sentToCut = value; Record.SentToCut = value; Notify(); }
            }

            public bool Cut
            {
                get => _cut;
                set { _cut = value; Record.Cut = value; Notify(); Notify(nameof(CutDateDisplay)); }
            }

            public DateTime? CutDate
            {
                get => _cutDate;
                set { _cutDate = value; Record.CutDate = value; Notify(); Notify(nameof(CutDateDisplay)); }
            }

            public string NestLocation
            {
                get => _nestLocation;
                set { _nestLocation = value; Record.NestLocation = value; Notify(); }
            }

            public string CutDateDisplay =>
                _cutDate.HasValue ? _cutDate.Value.ToString("yyyy-MM-dd") : "—";

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

            private void Notify([System.Runtime.CompilerServices.CallerMemberName] string prop = null)
                => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(prop));
        }
    }
}
