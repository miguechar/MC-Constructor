using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FirstAcadPlugin
{
    public class ProjectDialog : Window
    {
        private TextBox connectionStringTextBox;
        private Button connectButton;
        private TextBlock statusText;
        private ListBox projectListBox;

        public Project SelectedProject { get; private set; }

        public ProjectDialog()
        {
            Title = "Open Project - MC Constructor";
            Width = 550;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 400;
            MinHeight = 400;

            var mainGrid = new Grid { Margin = new Thickness(16) };

            // Define rows
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int row = 0;

            // Connection String Label
            var connLabel = new TextBlock
            {
                Text = "Database Connection String:",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(connLabel, row++);
            mainGrid.Children.Add(connLabel);

            // Connection String TextBox
            connectionStringTextBox = new TextBox
            {
                Text = "Host=localhost;Port=5432;Database=your_db;Username=postgres;Password=your_password",
                FontSize = 12,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false,
                Height = 60,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(connectionStringTextBox, row++);
            mainGrid.Children.Add(connectionStringTextBox);

            // Connect Button
            connectButton = new Button
            {
                Content = "Connect & Load Projects",
                FontSize = 12,
                Padding = new Thickness(16, 8, 16, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            connectButton.Click += ConnectButton_Click;
            Grid.SetRow(connectButton, row++);
            mainGrid.Children.Add(connectButton);

            // Status Text
            statusText = new TextBlock
            {
                Text = "Enter your database connection string and click Connect",
                FontSize = 12,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(statusText, row++);
            mainGrid.Children.Add(statusText);

            // Projects Label
            var projectsLabel = new TextBlock
            {
                Text = "Select a Project:",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(projectsLabel, row++);
            mainGrid.Children.Add(projectsLabel);

            // Projects ListBox
            projectListBox = new ListBox
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 16)
            };
            projectListBox.MouseDoubleClick += (s, e) =>
            {
                if (projectListBox.SelectedItem != null) SelectProject();
            };
            Grid.SetRow(projectListBox, row++);
            mainGrid.Children.Add(projectListBox);

            // Button Panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var selectButton = new Button
            {
                Content = "Select Project",
                FontSize = 12,
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            selectButton.Click += (s, e) => SelectProject();
            buttonPanel.Children.Add(selectButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                FontSize = 12,
                Padding = new Thickness(20, 8, 20, 8),
                IsCancel = true
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, row);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                statusText.Text = "Connecting...";
                statusText.Foreground = Brushes.Gray;

                DatabaseService.SetConnectionString(connectionStringTextBox.Text);

                string errorMessage;
                if (!DatabaseService.TestConnection(out errorMessage))
                {
                    statusText.Text = $"Connection failed:\n{errorMessage}";
                    statusText.Foreground = Brushes.Red;
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
                    statusText.Foreground = Brushes.Orange;
                }
                else
                {
                    statusText.Text = $"Connected! Found {projects.Count} project(s). Select one below.";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 0));
                }
            }
            catch (System.Exception ex)
            {
                statusText.Text = $"Error:\n{ex.Message}";
                statusText.Foreground = Brushes.Red;
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
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 12)
            };
            mainStack.Children.Add(passwordBox);

            // Status
            statusText = new TextBlock
            {
                Text = "",
                FontSize = 11,
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

            var testBtn = new Button { Content = "Test", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
            testBtn.Click += TestButton_Click;
            buttonPanel.Children.Add(testBtn);

            var saveBtn = new Button { Content = "Save", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            saveBtn.Click += SaveButton_Click;
            buttonPanel.Children.Add(saveBtn);

            var cancelBtn = new Button { Content = "Cancel", Padding = new Thickness(16, 6, 16, 6), IsCancel = true };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelBtn);

            mainStack.Children.Add(buttonPanel);
            Content = mainStack;
        }

        private TextBlock MakeLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        private TextBox MakeTextBox(string defaultText)
        {
            return new TextBox
            {
                Text = defaultText,
                FontSize = 12,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                statusText.Text = "Testing...";
                statusText.Foreground = Brushes.Gray;

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
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 0));
                }
                else
                {
                    statusText.Text = $"Failed: {errorMessage}";
                    statusText.Foreground = Brushes.Red;
                }
            }
            catch (System.Exception ex)
            {
                statusText.Text = $"Error: {ex.Message}";
                statusText.Foreground = Brushes.Red;
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
