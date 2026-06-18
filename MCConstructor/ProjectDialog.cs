using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MCConstructor
{
    public class ProjectDialog : Window
    {
        private TextBox connectionStringTextBox;
        private Button connectButton;
        private TextBlock statusText;
        private ListBox projectListBox;

        // JSON section controls
        private TextBox jsonPathTextBox;

        public Project SelectedProject { get; private set; }

        public ProjectDialog()
        {
            Title = "Open Project - MC Constructor";
            Width = 580;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 420;
            MinHeight = 500;
            Background = D(45, 45, 48);

            var mainGrid = new Grid { Margin = new Thickness(16) };

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: DB label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: DB textbox
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Connect button
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Status
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Divider
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5: JSON label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 6: JSON path row
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 7: Open JSON button
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 8: Projects label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 9: Project list
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 10: Buttons

            int row = 0;

            // ---- PostgreSQL section ----
            var connLabel = new TextBlock
            {
                Text = "Database Connection String:",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = D(200, 200, 200),
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(connLabel, row++);
            mainGrid.Children.Add(connLabel);

            connectionStringTextBox = new TextBox
            {
                Text = "Host=localhost;Port=5432;Database=your_db;Username=postgres;Password=your_password",
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false,
                Height = 60,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = D(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
            };
            Grid.SetRow(connectionStringTextBox, row++);
            mainGrid.Children.Add(connectionStringTextBox);

            connectButton = new Button
            {
                Content = "Connect & Load Projects",
                FontSize = 12,
                Height = 28, Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
                Background = D(0, 122, 204),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
            };
            connectButton.Click += ConnectButton_Click;
            Grid.SetRow(connectButton, row++);
            mainGrid.Children.Add(connectButton);

            statusText = new TextBlock
            {
                Text = "Enter your database connection string and click Connect",
                FontSize = 12,
                Foreground = D(160, 160, 165),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(statusText, row++);
            mainGrid.Children.Add(statusText);

            // ---- Divider ----
            var dividerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 4, 0, 12)
            };
            dividerPanel.Children.Add(new Separator { Width = 200, VerticalAlignment = VerticalAlignment.Center, Background = D(67, 67, 70) });
            dividerPanel.Children.Add(new TextBlock
            {
                Text = "  —  OR  —  ",
                FontSize = 12,
                Foreground = D(160, 160, 165),
                VerticalAlignment = VerticalAlignment.Center
            });
            dividerPanel.Children.Add(new Separator { Width = 200, VerticalAlignment = VerticalAlignment.Center, Background = D(67, 67, 70) });
            Grid.SetRow(dividerPanel, row++);
            mainGrid.Children.Add(dividerPanel);

            // ---- JSON section ----
            var jsonLabel = new TextBlock
            {
                Text = "Open Project File (.json):",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = D(200, 200, 200),
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(jsonLabel, row++);
            mainGrid.Children.Add(jsonLabel);

            // JSON path row: Browse button + path textbox
            var jsonPathRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            jsonPathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            jsonPathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var browseButton = new Button
            {
                Content = "Browse...",
                FontSize = 12,
                Height = 28, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 0),
                Background = D(70, 70, 75), Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            browseButton.Click += BrowseButton_Click;
            Grid.SetColumn(browseButton, 0);
            jsonPathRow.Children.Add(browseButton);

            jsonPathTextBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                IsReadOnly = true,
                Background = D(37, 37, 38), Foreground = D(160, 160, 165),
                BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(jsonPathTextBox, 1);
            jsonPathRow.Children.Add(jsonPathTextBox);

            Grid.SetRow(jsonPathRow, row++);
            mainGrid.Children.Add(jsonPathRow);

            var openJsonButton = new Button
            {
                Content = "Open Project",
                FontSize = 12,
                Height = 28, Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 16),
                Background = D(0, 122, 204),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
            };
            openJsonButton.Click += OpenJsonButton_Click;
            Grid.SetRow(openJsonButton, row++);
            mainGrid.Children.Add(openJsonButton);

            // ---- Project list (for postgres mode) ----
            var projectsLabel = new TextBlock
            {
                Text = "Select a Project:",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = D(200, 200, 200),
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(projectsLabel, row++);
            mainGrid.Children.Add(projectsLabel);

            projectListBox = new ListBox
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 16),
                Background = D(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D(67, 67, 70), BorderThickness = new Thickness(1),
            };
            projectListBox.MouseDoubleClick += (s, e) =>
            {
                if (projectListBox.SelectedItem != null) SelectProject();
            };
            Grid.SetRow(projectListBox, row++);
            mainGrid.Children.Add(projectListBox);

            // ---- Footer buttons ----
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var selectButton = new Button
            {
                Content = "Select Project",
                FontSize = 12,
                Height = 28, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                IsDefault = true,
                Background = D(0, 122, 204), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold,
            };
            selectButton.Click += (s, e) => SelectProject();
            buttonPanel.Children.Add(selectButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                FontSize = 12,
                Height = 28, Padding = new Thickness(12, 0, 12, 0),
                IsCancel = true,
                Background = D(70, 70, 75), Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, row);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }

        private static SolidColorBrush D(byte r, byte g, byte b)
            => new SolidColorBrush(Color.FromRgb(r, g, b));

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select project file",
                Filter = "Project Files (*.json)|*.json|All Files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
                jsonPathTextBox.Text = dlg.FileName;
        }

        private void OpenJsonButton_Click(object sender, RoutedEventArgs e)
        {
            string path = jsonPathTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("Please browse for a pro.json file first.", "No File Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var pf = ProjectFileWriter.TryLoad(path);
            if (pf?.Project == null)
            {
                statusText.Text = "Could not read the selected file. Make sure it is a valid pro.json.";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 70, 70));
                return;
            }

            if (pf.StorageMode == "json")
            {
                // JSON-only project: activate JSON provider and return immediately.
                StorageRouter.UseJson(path);

                SelectedProject = new Project
                {
                    Id = Guid.Parse(pf.Project.Id),
                    Name = pf.Project.Name,
                    Description = pf.Project.Description,
                    Directory = pf.Project.Directory
                };

                DialogResult = true;
                Close();
            }
            else if (pf.StorageMode == "postgres")
            {
                // Postgres-backed project: auto-connect using stored credentials.
                if (pf.Database == null)
                {
                    statusText.Text = "The project file has no database credentials. Enter the connection string manually above.";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 150, 0));
                    return;
                }

                string cs = ProjectFileWriter.BuildConnectionString(pf.Database);
                try
                {
                    statusText.Text = "Connecting...";
                    statusText.Foreground = D(160, 160, 165);

                    StorageRouter.UseNpgsql(cs);

                    string connErr;
                    if (!DatabaseService.TestConnection(out connErr))
                    {
                        statusText.Text = $"Connection failed:\n{connErr}";
                        statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 70, 70));
                        return;
                    }

                    var projects = DatabaseService.GetProjects();
                    projectListBox.Items.Clear();
                    foreach (var p in projects)
                        projectListBox.Items.Add(p);

                    // Auto-select the project matching the JSON file's project id.
                    string targetId = pf.Project.Id;
                    foreach (var item in projectListBox.Items)
                    {
                        if (item is Project proj && proj.Id.ToString() == targetId)
                        {
                            projectListBox.SelectedItem = proj;
                            break;
                        }
                    }

                    if (projectListBox.SelectedItem != null)
                    {
                        // Auto-confirm if we found the right project.
                        SelectProject();
                    }
                    else
                    {
                        statusText.Text = $"Connected! Found {projects.Count} project(s). Select one below.";
                        statusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 80));
                    }
                }
                catch (Exception ex)
                {
                    statusText.Text = $"Error:\n{ex.Message}";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 70, 70));
                }
            }
            else
            {
                statusText.Text = $"Unknown storageMode '{pf.StorageMode}' in the project file.";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 70, 70));
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                statusText.Text = "Connecting...";
                statusText.Foreground = D(160, 160, 165);

                DatabaseService.SetConnectionString(connectionStringTextBox.Text);

                string errorMessage;
                if (!DatabaseService.TestConnection(out errorMessage))
                {
                    statusText.Text = $"Connection failed:\n{errorMessage}";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 70, 70));
                    return;
                }

                var projects = DatabaseService.GetProjects();

                projectListBox.Items.Clear();
                foreach (var project in projects)
                {
                    projectListBox.Items.Add(project);
                }

                if (projects.Count == 0)
                {
                    statusText.Text = "Connected! But no projects found in the database.\nMake sure your 'projects' table has data.";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 150, 0));
                }
                else
                {
                    statusText.Text = $"Connected! Found {projects.Count} project(s). Select one below.";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 80));
                }
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error:\n{ex.Message}";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 70, 70));
            }
        }

        private void SelectProject()
        {
            if (projectListBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a project from the list.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedProject = projectListBox.SelectedItem as Project;
            DialogResult = true;
            Close();
        }
    }

    public class DatabaseConfigDialog : Window
    {
        private TextBox hostTextBox;
        private TextBox portTextBox;
        private TextBox databaseTextBox;
        private TextBox usernameTextBox;
        private PasswordBox passwordBox;
        private TextBlock statusText;

        public DatabaseConfigDialog()
        {
            Title = "Database Configuration";
            Width = 420;
            Height = 380;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = D2(45, 45, 48);

            var mainStack = new StackPanel { Margin = new Thickness(20) };

            // Host
            mainStack.Children.Add(MakeLabel("Host:"));
            hostTextBox = MakeTextBox("localhost");
            mainStack.Children.Add(hostTextBox);

            // Port
            mainStack.Children.Add(MakeLabel("Port:"));
            portTextBox = MakeTextBox("5432");
            mainStack.Children.Add(portTextBox);

            // Database
            mainStack.Children.Add(MakeLabel("Database:"));
            databaseTextBox = MakeTextBox("");
            mainStack.Children.Add(databaseTextBox);

            // Username
            mainStack.Children.Add(MakeLabel("Username:"));
            usernameTextBox = MakeTextBox("postgres");
            mainStack.Children.Add(usernameTextBox);

            // Password
            mainStack.Children.Add(MakeLabel("Password:"));
            passwordBox = new PasswordBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 12),
                Background = D2(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D2(67, 67, 70), BorderThickness = new Thickness(1),
            };
            mainStack.Children.Add(passwordBox);

            // Status
            statusText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = D2(160, 160, 165),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            mainStack.Children.Add(statusText);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var testBtn = new Button
            {
                Content = "Test", Height = 28, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = D2(70, 70, 75), Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            testBtn.Click += TestButton_Click;
            buttonPanel.Children.Add(testBtn);

            var saveBtn = new Button
            {
                Content = "Save", Height = 28, Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                IsDefault = true,
                Background = D2(0, 122, 204), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold,
            };
            saveBtn.Click += SaveButton_Click;
            buttonPanel.Children.Add(saveBtn);

            var cancelBtn = new Button
            {
                Content = "Cancel", Height = 28, Padding = new Thickness(12, 0, 12, 0),
                IsCancel = true,
                Background = D2(70, 70, 75), Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelBtn);

            mainStack.Children.Add(buttonPanel);
            Content = mainStack;
        }

        private static SolidColorBrush D2(byte r, byte g, byte b)
            => new SolidColorBrush(Color.FromRgb(r, g, b));

        private TextBlock MakeLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = D2(200, 200, 200),
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        private TextBox MakeTextBox(string defaultText)
        {
            return new TextBox
            {
                Text = defaultText,
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 12),
                Background = D2(37, 37, 38), Foreground = Brushes.White,
                BorderBrush = D2(67, 67, 70), BorderThickness = new Thickness(1),
            };
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                statusText.Text = "Testing...";
                statusText.Foreground = D2(160, 160, 165);

                DatabaseService.SetConnectionString(
                    hostTextBox.Text,
                    int.Parse(portTextBox.Text),
                    databaseTextBox.Text,
                    usernameTextBox.Text,
                    passwordBox.Password
                );

                string errorMessage;
                if (DatabaseService.TestConnection(out errorMessage))
                {
                    statusText.Text = "Connection successful!";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 80));
                }
                else
                {
                    statusText.Text = $"Failed: {errorMessage}";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 70, 70));
                }
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error: {ex.Message}";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 70, 70));
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            DatabaseService.SetConnectionString(
                hostTextBox.Text,
                int.Parse(portTextBox.Text),
                databaseTextBox.Text,
                usernameTextBox.Text,
                passwordBox.Password
            );
            DialogResult = true;
            Close();
        }
    }
}
