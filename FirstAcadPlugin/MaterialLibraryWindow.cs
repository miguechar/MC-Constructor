using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Project-scoped material library editor. Two tabs:
    ///
    ///   Materials  Lists materials with name + density. Selecting a
    ///              material reveals its plate stock (code, dimensions,
    ///              default flag) which can be added/edited/removed in
    ///              place.
    ///
    ///   Profiles   Lists profiles for the project. Each profile
    ///              references a material, an optional cross-section
    ///              drawing (drawing_type='Profile' under
    ///              01 Standards/Profiles), and an optional density
    ///              override.
    ///
    /// Edits are committed immediately - no separate "Save" button -
    /// because the workflow is "open, tweak, close" rather than building
    /// up large transactional changes. The Refresh button reloads from the
    /// database in case another instance modified the same project.
    /// </summary>
    public class MaterialLibraryWindow : Window
    {
        // Materials tab
        private ListBox materialsList;
        private TextBox materialNameBox;
        private TextBox materialDensityBox;
        private TextBox materialDescBox;
        private DataGrid platesGrid;

        // Profiles tab
        private ListBox profilesList;
        private TextBox profileNameBox;
        private TextBox profileCodeBox;
        private TextBox profileDescBox;
        private ComboBox profileMaterialCombo;
        private ComboBox profileDrawingCombo;
        private TextBox profileDensityOverrideBox;

        // Cached state shared across the two tabs.
        private List<Material> materials = new List<Material>();
        private List<Profile> profiles = new List<Profile>();

        public MaterialLibraryWindow()
        {
            Title = "Material Library";
            Width = 880;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 760;
            MinHeight = 480;

            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            // Header with project name + refresh.
            var header = new DockPanel();
            header.Children.Add(new TextBlock
            {
                Text = DatabaseService.CurrentProject != null
                    ? $"Project: {DatabaseService.CurrentProject.Name}"
                    : "(no project open)",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            });
            var refreshBtn = new Button
            {
                Content = "Refresh",
                Padding = new Thickness(12, 4, 12, 4),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            refreshBtn.Click += (s, e) => ReloadAll();
            DockPanel.SetDock(refreshBtn, Dock.Right);
            header.Children.Add(refreshBtn);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Tabs
            var tabs = new TabControl();
            tabs.Items.Add(BuildMaterialsTab());
            tabs.Items.Add(BuildProfilesTab());
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            // Footer close button
            var closeBtn = new Button
            {
                Content = "Close",
                Padding = new Thickness(20, 6, 20, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
                IsCancel = true
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetRow(closeBtn, 2);
            root.Children.Add(closeBtn);

            Content = root;

            // Block use when no project is open - the whole library is per-project.
            if (DatabaseService.CurrentProject == null)
            {
                tabs.IsEnabled = false;
                refreshBtn.IsEnabled = false;
            }
            else
            {
                Loaded += (s, e) => ReloadAll();
            }
        }

        // ====================================================================
        // MATERIALS TAB
        // ====================================================================

        private TabItem BuildMaterialsTab()
        {
            var tab = new TabItem { Header = "Materials" };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left column: list + add/delete buttons
            var leftStack = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };

            var leftButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var addMatBtn = new Button { Content = "+ Add", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 4, 0) };
            addMatBtn.Click += (s, e) => OnAddMaterial();
            leftButtons.Children.Add(addMatBtn);

            var delMatBtn = new Button { Content = "Delete", Padding = new Thickness(10, 3, 10, 3) };
            delMatBtn.Click += (s, e) => OnDeleteMaterial();
            leftButtons.Children.Add(delMatBtn);

            DockPanel.SetDock(leftButtons, Dock.Top);
            leftStack.Children.Add(leftButtons);

            materialsList = new ListBox { DisplayMemberPath = "Name" };
            materialsList.SelectionChanged += (s, e) => OnMaterialSelectionChanged();
            leftStack.Children.Add(materialsList);

            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // Right column: form for selected material + plates list
            var rightStack = new DockPanel();

            var formGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 4; i++)
                formGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            AddFormRow(formGrid, 0, "Name:",        materialNameBox    = NewTextBox());
            AddFormRow(formGrid, 1, "Density (kg/m^3):", materialDensityBox = NewTextBox("7850"));
            AddFormRow(formGrid, 2, "Description:", materialDescBox    = NewTextBox());

            var saveMatRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 6, 0, 0)
            };
            var saveMatBtn = new Button { Content = "Save Material", Padding = new Thickness(14, 4, 14, 4) };
            saveMatBtn.Click += (s, e) => OnSaveMaterial();
            saveMatRow.Children.Add(saveMatBtn);
            Grid.SetRow(saveMatRow, 3);
            Grid.SetColumnSpan(saveMatRow, 2);
            formGrid.Children.Add(saveMatRow);

            DockPanel.SetDock(formGrid, Dock.Top);
            rightStack.Children.Add(formGrid);

            // Plates section
            var platesHeader = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
            platesHeader.Children.Add(new TextBlock
            {
                Text = "Stock Plates",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            var platesButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var addPlateBtn = new Button { Content = "+ Add Plate", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 4, 0) };
            addPlateBtn.Click += (s, e) => OnAddPlate();
            platesButtons.Children.Add(addPlateBtn);
            var delPlateBtn = new Button { Content = "Delete Plate", Padding = new Thickness(10, 3, 10, 3) };
            delPlateBtn.Click += (s, e) => OnDeletePlate();
            platesButtons.Children.Add(delPlateBtn);
            DockPanel.SetDock(platesButtons, Dock.Right);
            platesHeader.Children.Add(platesButtons);
            DockPanel.SetDock(platesHeader, Dock.Top);
            rightStack.Children.Add(platesHeader);

            // DataGrid for plates - inline-editable so the user doesn't have
            // to click into a sub-dialog for every change.
            platesGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,        // Add via "+ Add Plate" button
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Single,
                IsReadOnly = false,
                RowHeight = 26
            };
            platesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Code", Binding = new System.Windows.Data.Binding("Code"),
                Width = new DataGridLength(70)
            });
            platesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Width (mm)", Binding = new System.Windows.Data.Binding("Width"),
                Width = new DataGridLength(100)
            });
            platesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Height (mm)", Binding = new System.Windows.Data.Binding("Height"),
                Width = new DataGridLength(100)
            });
            platesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Thickness (mm)", Binding = new System.Windows.Data.Binding("Thickness"),
                Width = new DataGridLength(110)
            });
            platesGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Default",
                Binding = new System.Windows.Data.Binding("IsDefault"),
                Width = new DataGridLength(70)
            });
            platesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Description", Binding = new System.Windows.Data.Binding("Description"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });

            // Persist edits as soon as the user leaves a cell. This keeps
            // the data model and the UI in sync without an explicit save.
            platesGrid.CellEditEnding += PlatesGrid_CellEditEnding;
            rightStack.Children.Add(platesGrid);

            Grid.SetColumn(rightStack, 1);
            grid.Children.Add(rightStack);

            tab.Content = grid;
            return tab;
        }

        private void OnMaterialSelectionChanged()
        {
            var m = materialsList.SelectedItem as Material;
            if (m == null)
            {
                materialNameBox.Text = "";
                materialDensityBox.Text = "7850";
                materialDescBox.Text = "";
                platesGrid.ItemsSource = null;
                return;
            }
            materialNameBox.Text    = m.Name;
            materialDensityBox.Text = m.Density.ToString("0.##");
            materialDescBox.Text    = m.Description ?? "";
            ReloadPlates(m);
        }

        private void ReloadPlates(Material m)
        {
            try
            {
                var plates = MaterialLibraryService.GetPlates(m.Id);
                // Bind to an ObservableCollection so DataGrid picks up adds/deletes.
                platesGrid.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<MaterialPlate>(plates);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not load plates: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnAddMaterial()
        {
            var m = new Material { Name = "New Material", Density = 7850 };
            try
            {
                MaterialLibraryService.SaveMaterial(m);
                ReloadMaterials();
                materialsList.SelectedItem = materials.FirstOrDefault(x => x.Id == m.Id);
                materialNameBox.Focus();
                materialNameBox.SelectAll();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not create material: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDeleteMaterial()
        {
            var m = materialsList.SelectedItem as Material;
            if (m == null) return;
            if (MessageBox.Show(
                    $"Delete material \"{m.Name}\"? This also removes its plates and unlinks any profiles using it.",
                    "Delete Material",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK) return;

            try
            {
                MaterialLibraryService.DeleteMaterial(m.Id);
                ReloadMaterials();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not delete: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSaveMaterial()
        {
            var m = materialsList.SelectedItem as Material;
            if (m == null) return;

            string name = materialNameBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Material name is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(materialDensityBox.Text, out double density) || density <= 0)
            {
                MessageBox.Show("Density must be a positive number (kg/m^3).", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            m.Name = name;
            m.Density = density;
            m.Description = string.IsNullOrWhiteSpace(materialDescBox.Text) ? null : materialDescBox.Text.Trim();

            try
            {
                MaterialLibraryService.SaveMaterial(m);
                ReloadMaterials();
                materialsList.SelectedItem = materials.FirstOrDefault(x => x.Id == m.Id);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not save: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnAddPlate()
        {
            var m = materialsList.SelectedItem as Material;
            if (m == null)
            {
                MessageBox.Show("Select a material first.", "Add Plate",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Generate a unique-ish default code (T01, T02, ...) so the
            // user can see the row land in the grid and just rename / edit
            // dimensions inline.
            var existing = MaterialLibraryService.GetPlates(m.Id);
            int next = 1;
            while (existing.Any(p => p.Code.Equals($"T{next:D2}", StringComparison.OrdinalIgnoreCase)))
                next++;

            var plate = new MaterialPlate
            {
                MaterialId = m.Id,
                Code = $"T{next:D2}",
                Width = 2438,    // 8 ft
                Height = 1219,   // 4 ft
                Thickness = 10,
                IsDefault = existing.Count == 0   // first plate becomes default
            };

            try
            {
                MaterialLibraryService.SavePlate(plate);
                ReloadPlates(m);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not add plate: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDeletePlate()
        {
            var plate = platesGrid.SelectedItem as MaterialPlate;
            var m = materialsList.SelectedItem as Material;
            if (plate == null || m == null) return;

            if (MessageBox.Show($"Delete plate \"{plate.Code}\"?",
                    "Delete Plate",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK) return;

            try
            {
                MaterialLibraryService.DeletePlate(plate.Id);
                ReloadPlates(m);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not delete: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlatesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            // Defer the persist call until the binding has copied the new
            // value back into the MaterialPlate instance, otherwise we'd
            // be saving the pre-edit state.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var plate = e.Row.Item as MaterialPlate;
                if (plate == null) return;
                try
                {
                    MaterialLibraryService.SavePlate(plate);
                    // If user just toggled IsDefault on, refresh to reflect
                    // the side-effect of clearing other defaults.
                    var m = materialsList.SelectedItem as Material;
                    if (m != null) ReloadPlates(m);
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Could not save plate: {ex.Message}",
                        "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    var m = materialsList.SelectedItem as Material;
                    if (m != null) ReloadPlates(m);   // revert UI to DB state
                }
            }));
        }

        // ====================================================================
        // PROFILES TAB
        // ====================================================================

        private TabItem BuildProfilesTab()
        {
            var tab = new TabItem { Header = "Profiles" };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: list + add/delete
            var leftStack = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };

            var leftButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var addProfBtn = new Button { Content = "+ Add", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 4, 0) };
            addProfBtn.Click += (s, e) => OnAddProfile();
            leftButtons.Children.Add(addProfBtn);

            var delProfBtn = new Button { Content = "Delete", Padding = new Thickness(10, 3, 10, 3) };
            delProfBtn.Click += (s, e) => OnDeleteProfile();
            leftButtons.Children.Add(delProfBtn);
            DockPanel.SetDock(leftButtons, Dock.Top);
            leftStack.Children.Add(leftButtons);

            profilesList = new ListBox { DisplayMemberPath = "Name" };
            profilesList.SelectionChanged += (s, e) => OnProfileSelectionChanged();
            leftStack.Children.Add(profilesList);

            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // Right: form
            var formGrid = new Grid();
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 8; i++)
                formGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            AddFormRow(formGrid, 0, "Name:",            profileNameBox      = NewTextBox());
            AddFormRow(formGrid, 1, "Code:",            profileCodeBox      = NewTextBox());
            AddFormRow(formGrid, 2, "Description:",     profileDescBox      = NewTextBox());

            profileMaterialCombo = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(6, 3, 6, 3),
                DisplayMemberPath = "Name"
            };
            AddFormRowControl(formGrid, 3, "Material:", profileMaterialCombo);

            profileDrawingCombo = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(6, 3, 6, 3),
                DisplayMemberPath = "Name"
            };
            AddFormRowControl(formGrid, 4, "Cross-Section\nDrawing:", profileDrawingCombo);

            AddFormRow(formGrid, 5, "Density override\n(kg/m^3, optional):",
                profileDensityOverrideBox = NewTextBox());

            // Hint about cross-section drawings.
            var hint = new TextBlock
            {
                Text = "Cross-section drawings live under <project>/01 Standards/Profiles. " +
                       "Create one with MCCreateDrawing using type 'Profile'.",
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 8)
            };
            Grid.SetRow(hint, 6);
            Grid.SetColumnSpan(hint, 2);
            formGrid.Children.Add(hint);

            var saveProfRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };
            var saveProfBtn = new Button { Content = "Save Profile", Padding = new Thickness(14, 4, 14, 4) };
            saveProfBtn.Click += (s, e) => OnSaveProfile();
            saveProfRow.Children.Add(saveProfBtn);
            Grid.SetRow(saveProfRow, 7);
            Grid.SetColumnSpan(saveProfRow, 2);
            formGrid.Children.Add(saveProfRow);

            Grid.SetColumn(formGrid, 1);
            grid.Children.Add(formGrid);

            tab.Content = grid;
            return tab;
        }

        private void OnProfileSelectionChanged()
        {
            var p = profilesList.SelectedItem as Profile;
            if (p == null)
            {
                profileNameBox.Text = "";
                profileCodeBox.Text = "";
                profileDescBox.Text = "";
                profileMaterialCombo.SelectedIndex = -1;
                profileDrawingCombo.SelectedIndex = -1;
                profileDensityOverrideBox.Text = "";
                return;
            }
            profileNameBox.Text             = p.Name;
            profileCodeBox.Text             = p.Code ?? "";
            profileDescBox.Text             = p.Description ?? "";
            profileDensityOverrideBox.Text  = p.DensityOverride?.ToString("0.##") ?? "";

            profileMaterialCombo.SelectedItem = materials.FirstOrDefault(m => m.Id == p.MaterialId);

            // The drawings list contains only Drawing objects; find by Id.
            foreach (var item in profileDrawingCombo.Items)
            {
                if (item is Drawing d && p.DrawingId.HasValue && d.Id == p.DrawingId.Value)
                {
                    profileDrawingCombo.SelectedItem = item;
                    return;
                }
            }
            profileDrawingCombo.SelectedIndex = 0; // (none)
        }

        private void OnAddProfile()
        {
            var p = new Profile { Name = "New Profile" };
            try
            {
                MaterialLibraryService.SaveProfile(p);
                ReloadProfiles();
                profilesList.SelectedItem = profiles.FirstOrDefault(x => x.Id == p.Id);
                profileNameBox.Focus();
                profileNameBox.SelectAll();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not create profile: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDeleteProfile()
        {
            var p = profilesList.SelectedItem as Profile;
            if (p == null) return;
            if (MessageBox.Show($"Delete profile \"{p.Name}\"?",
                    "Delete Profile",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            try
            {
                MaterialLibraryService.DeleteProfile(p.Id);
                ReloadProfiles();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not delete: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSaveProfile()
        {
            var p = profilesList.SelectedItem as Profile;
            if (p == null) return;

            string name = profileNameBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Profile name is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double? densityOverride = null;
            if (!string.IsNullOrWhiteSpace(profileDensityOverrideBox.Text))
            {
                if (!double.TryParse(profileDensityOverrideBox.Text, out double d) || d <= 0)
                {
                    MessageBox.Show("Density override must be a positive number, or empty to use the material's density.",
                        "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                densityOverride = d;
            }

            p.Name             = name;
            p.Code             = string.IsNullOrWhiteSpace(profileCodeBox.Text) ? null : profileCodeBox.Text.Trim();
            p.Description      = string.IsNullOrWhiteSpace(profileDescBox.Text) ? null : profileDescBox.Text.Trim();
            p.MaterialId       = (profileMaterialCombo.SelectedItem as Material)?.Id;
            p.DrawingId        = (profileDrawingCombo.SelectedItem as Drawing)?.Id;
            p.DensityOverride  = densityOverride;

            try
            {
                MaterialLibraryService.SaveProfile(p);
                ReloadProfiles();
                profilesList.SelectedItem = profiles.FirstOrDefault(x => x.Id == p.Id);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not save profile: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ====================================================================
        // RELOADERS
        // ====================================================================

        private void ReloadAll()
        {
            ReloadMaterials();
            ReloadProfileDrawingCombo();
            ReloadProfiles();
        }

        private void ReloadMaterials()
        {
            try
            {
                materials = MaterialLibraryService.GetMaterials();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not load materials: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                materials = new List<Material>();
            }

            var keepId = (materialsList?.SelectedItem as Material)?.Id;
            materialsList.ItemsSource = materials;
            if (keepId.HasValue)
            {
                materialsList.SelectedItem = materials.FirstOrDefault(m => m.Id == keepId.Value);
            }
            else if (materials.Count > 0)
            {
                materialsList.SelectedIndex = 0;
            }

            // Refresh the material combo on the profiles tab.
            if (profileMaterialCombo != null)
            {
                var keepMatId = (profileMaterialCombo.SelectedItem as Material)?.Id;
                profileMaterialCombo.ItemsSource = materials;
                profileMaterialCombo.SelectedItem = materials.FirstOrDefault(m => m.Id == keepMatId);
            }
        }

        private void ReloadProfileDrawingCombo()
        {
            try
            {
                var drawings = MaterialLibraryService.GetProfileDrawings();
                profileDrawingCombo.Items.Clear();
                profileDrawingCombo.Items.Add(new ComboPlaceholder()); // (none)
                foreach (var d in drawings)
                {
                    profileDrawingCombo.Items.Add(d);
                }
                profileDrawingCombo.SelectedIndex = 0;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not load profile drawings: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReloadProfiles()
        {
            try
            {
                profiles = MaterialLibraryService.GetProfiles();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Could not load profiles: {ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                profiles = new List<Profile>();
            }

            var keepId = (profilesList?.SelectedItem as Profile)?.Id;
            profilesList.ItemsSource = profiles;
            if (keepId.HasValue)
                profilesList.SelectedItem = profiles.FirstOrDefault(p => p.Id == keepId.Value);
            else if (profiles.Count > 0)
                profilesList.SelectedIndex = 0;
        }

        // ====================================================================
        // SMALL HELPERS
        // ====================================================================

        private static TextBox NewTextBox(string defaultText = "")
        {
            return new TextBox
            {
                Text = defaultText,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(6, 3, 6, 3),
                FontSize = 12
            };
        }

        private static void AddFormRow(Grid grid, int row, string label, FrameworkElement field)
        {
            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 6),
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
        }

        private static void AddFormRowControl(Grid grid, int row, string label, FrameworkElement field)
            => AddFormRow(grid, row, label, field);

        /// <summary>
        /// Rendered as "(none)" in the cross-section drawing combo so the
        /// user can clear the link without having to delete the row.
        /// </summary>
        private class ComboPlaceholder
        {
            public string Name => "(none)";
            public override string ToString() => Name;
        }
    }
}
