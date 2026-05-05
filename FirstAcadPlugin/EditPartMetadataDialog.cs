using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Pre-filled dialog for editing all metadata on a single 3D solid.
    /// Shows read-only geometry info and lets the user update Part Name,
    /// Material, and Profile.
    /// </summary>
    public class EditPartMetadataDialog : Window
    {
        // ── Outputs ────────────────────────────────────────────────────────────
        public string   NewPartName          { get; private set; }
        public Material SelectedMaterial     { get; private set; }   // null = no material
        public string   SelectedProfileName  { get; private set; }   // "" = no profile

        // ── Controls ───────────────────────────────────────────────────────────
        private readonly TextBox  _partNameBox;
        private readonly ComboBox _materialCombo;
        private readonly ComboBox _profileCombo;

        public EditPartMetadataDialog(
            // current XData values
            string currentPartName,
            string currentMaterialId,
            string currentMaterialName,
            string currentProfileName,
            // read-only display
            string handle,
            string layer,
            double width,
            double depth,
            double height,
            // library data
            List<Material> materials,
            List<string> profileNames)
        {
            Title  = "Edit Part Metadata";
            Width  = 420;
            Height = 420;
            MinWidth  = 360;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = Brush(45, 45, 48);

            var root = new Grid { Margin = new Thickness(18) };
            for (int i = 0; i < 9; i++)
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int row = 0;

            // Header
            var header = new TextBlock
            {
                Text = "Edit Part Metadata",
                FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 14),
            };
            Grid.SetRow(header, row); Grid.SetColumnSpan(header, 2);
            root.Children.Add(header);
            row++;

            // ── Read-only info ────────────────────────────────────────────────
            SectionLabel(root, row++, "GEOMETRY");

            InfoRow(root, row++, "Handle:",     handle);
            InfoRow(root, row++, "Layer:",      layer);
            InfoRow(root, row++, "Dimensions:", $"{width:0.##} × {depth:0.##} × {height:0.##} mm");

            // Divider
            var div = new Border
            {
                Height = 1, Background = Brush(67, 67, 70),
                Margin = new Thickness(0, 10, 0, 10),
            };
            Grid.SetRow(div, row); Grid.SetColumnSpan(div, 2);
            root.Children.Add(div);
            row++;

            // ── Editable fields ───────────────────────────────────────────────
            SectionLabel(root, row++, "METADATA");

            // Part Name
            FieldLabel(root, row, "Part Name:");
            _partNameBox = new TextBox
            {
                Text = currentPartName ?? "",
                Height = 28, Padding = new Thickness(6, 4, 6, 4),
                Background = Brush(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = Brush(67, 67, 70), BorderThickness = new Thickness(1),
                FontSize = 13, Margin = new Thickness(0, 4, 0, 8),
            };
            Grid.SetRow(_partNameBox, row); Grid.SetColumn(_partNameBox, 1);
            root.Children.Add(_partNameBox);
            row++;

            // Material
            FieldLabel(root, row, "Material:");
            _materialCombo = new ComboBox
            {
                Height = 28, Margin = new Thickness(0, 4, 0, 8),
                Background = Brush(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = Brush(67, 67, 70),
            };
            _materialCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = null });
            int matSelectIdx = 0;
            foreach (var m in materials)
            {
                var item = new ComboBoxItem { Content = m.Name, Tag = m };
                _materialCombo.Items.Add(item);
                if (m.Id.ToString() == currentMaterialId)
                    matSelectIdx = _materialCombo.Items.Count - 1;
            }
            _materialCombo.SelectedIndex = matSelectIdx;
            Grid.SetRow(_materialCombo, row); Grid.SetColumn(_materialCombo, 1);
            root.Children.Add(_materialCombo);
            row++;

            // Profile
            FieldLabel(root, row, "Profile:");
            _profileCombo = new ComboBox
            {
                Height = 28, Margin = new Thickness(0, 4, 0, 8),
                Background = Brush(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = Brush(67, 67, 70),
            };
            _profileCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = "" });
            int profSelectIdx = 0;
            foreach (var pname in profileNames)
            {
                var item = new ComboBoxItem { Content = pname, Tag = pname };
                _profileCombo.Items.Add(item);
                if (pname == currentProfileName)
                    profSelectIdx = _profileCombo.Items.Count - 1;
            }
            _profileCombo.SelectedIndex = profSelectIdx;
            Grid.SetRow(_profileCombo, row); Grid.SetColumn(_profileCombo, 1);
            root.Children.Add(_profileCombo);
            row++;

            // Buttons (row = star spacer + last row)
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            Grid.SetRow(btnRow, root.RowDefinitions.Count - 1);
            Grid.SetColumnSpan(btnRow, 2);
            root.Children.Add(btnRow);

            btnRow.Children.Add(DlgBtn("Cancel", false, (s, e) => { DialogResult = false; Close(); }));
            btnRow.Children.Add(DlgBtn("Apply",  true,  OnApply));

            Content = root;
            Loaded += (s, e) => { _partNameBox.Focus(); _partNameBox.SelectAll(); };
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            NewPartName = _partNameBox.Text.Trim();

            if (_materialCombo.SelectedItem is ComboBoxItem matItem && matItem.Tag is Material mat)
                SelectedMaterial = mat;
            else
                SelectedMaterial = null;

            if (_profileCombo.SelectedItem is ComboBoxItem profItem)
                SelectedProfileName = (profItem.Tag as string) ?? "";
            else
                SelectedProfileName = "";

            DialogResult = true;
            Close();
        }

        // ── Layout helpers ────────────────────────────────────────────────────

        private void SectionLabel(Grid g, int row, string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(130, 160, 200)),
                FontSize = 10, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 4),
            };
            Grid.SetRow(tb, row); Grid.SetColumnSpan(tb, 2);
            g.Children.Add(tb);
        }

        private void InfoRow(Grid g, int row, string label, string value)
        {
            FieldLabel(g, row, label);
            var val = new TextBlock
            {
                Text = value,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4),
                FontSize = 12,
            };
            Grid.SetRow(val, row); Grid.SetColumn(val, 1);
            g.Children.Add(val);
        }

        private static void FieldLabel(Grid g, int row, string text)
        {
            var lbl = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4),
                FontSize = 12,
            };
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
            g.Children.Add(lbl);
        }

        private static Button DlgBtn(string text, bool isDefault, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = text, Height = 30, Padding = new Thickness(16, 0, 16, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Background = isDefault ? Brush(0, 122, 204) : Brush(60, 60, 65),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontWeight = isDefault ? FontWeights.SemiBold : FontWeights.Normal,
                IsDefault = isDefault, IsCancel = !isDefault,
            };
            b.Click += click;
            return b;
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b)
            => new SolidColorBrush(Color.FromRgb(r, g, b));
    }
}
