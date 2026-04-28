using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using System;
using System.IO;
using System.Reflection;

// Alias to avoid conflicts
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FirstAcadPlugin
{
    /// <summary>
    /// This class initializes the plugin when AutoCAD loads it.
    /// It creates the "MC Constructor" ribbon tab with buttons.
    /// </summary>
    public class MyPlugin : IExtensionApplication
    {
        private static bool _assemblyResolverRegistered = false;

        /// <summary>
        /// Called when the plugin is loaded into AutoCAD.
        /// </summary>
        public void Initialize()
        {
            // Register assembly resolver to handle version mismatches
            if (!_assemblyResolverRegistered)
            {
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                _assemblyResolverRegistered = true;
            }

            // Wait for AutoCAD to be fully ready before creating the ribbon
            AcadApp.Idle += OnApplicationIdle;
        }

        /// <summary>
        /// Handles assembly resolution for dependencies that have version mismatches.
        /// </summary>
        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            // Get the assembly name being requested
            var assemblyName = new AssemblyName(args.Name);
            string name = assemblyName.Name;

            // Get the directory where our plugin is located
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // Try to load from the plugin directory
            string assemblyPath = Path.Combine(pluginDir, name + ".dll");

            if (File.Exists(assemblyPath))
            {
                try
                {
                    return Assembly.LoadFrom(assemblyPath);
                }
                catch
                {
                    // Failed to load, return null
                }
            }

            return null;
        }

        /// <summary>
        /// Called when AutoCAD is idle and ready.
        /// </summary>
        private void OnApplicationIdle(object sender, EventArgs e)
        {
            // Only run once - unsubscribe immediately
            AcadApp.Idle -= OnApplicationIdle;

            // Create our custom ribbon
            CreateRibbon();

            // Initialize the metadata palette (hidden by default)
            MetadataPalette.Initialize();
        }

        /// <summary>
        /// Creates the "MC Constructor" ribbon tab with buttons.
        /// </summary>
        private void CreateRibbon()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;

            if (ribbon == null)
                return;

            // Create a new ribbon tab called "MC Constructor"
            RibbonTab tab = new RibbonTab();
            tab.Title = "MC Constructor";
            tab.Id = "MC_CONSTRUCTOR_TAB";
            ribbon.Tabs.Add(tab);

            // ========== PROJECT PANEL ==========
            RibbonPanelSource projectPanelSource = new RibbonPanelSource();
            projectPanelSource.Title = "Project";

            RibbonPanel projectPanel = new RibbonPanel();
            projectPanel.Source = projectPanelSource;
            tab.Panels.Add(projectPanel);

            projectPanelSource.Items.Add(CreateLargeButton("Open\nProject", "MC_OPEN_PROJECT"));
            projectPanelSource.Items.Add(CreateLargeButton("Save\nParts", "MC_SAVE_PARTS"));
            projectPanelSource.Items.Add(CreateLargeButton("Project\nStatus", "MC_PROJECT_STATUS"));

            // ========== PARTS PANEL ==========
            RibbonPanelSource partsPanelSource = new RibbonPanelSource();
            partsPanelSource.Title = "Parts";

            RibbonPanel partsPanel = new RibbonPanel();
            partsPanel.Source = partsPanelSource;
            tab.Panels.Add(partsPanel);

            partsPanelSource.Items.Add(CreateLargeButton("Add\nMetadata", "MC_ADD_PART_METADATA"));
            partsPanelSource.Items.Add(CreateLargeButton("View\nMetadata", "MC_VIEW_PART_METADATA"));
            partsPanelSource.Items.Add(CreateLargeButton("List\nParts", "MC_LIST_PARTS"));

            // ========== REFERENCES PANEL ==========
            RibbonPanelSource refsPanelSource = new RibbonPanelSource();
            refsPanelSource.Title = "References";

            RibbonPanel refsPanel = new RibbonPanel();
            refsPanel.Source = refsPanelSource;
            tab.Panels.Add(refsPanel);

            refsPanelSource.Items.Add(CreateLargeButton("Insert\nPart", "MC_INSERT_PART"));
            refsPanelSource.Items.Add(CreateLargeButton("Override\nPart", "MC_OVERRIDE_PART"));
            refsPanelSource.Items.Add(CreateLargeButton("Update\nAll Parts", "MC_UPDATE_ALL_PARTS"));
            refsPanelSource.Items.Add(CreateLargeButton("List DB\nParts", "MC_LIST_DB_PARTS"));

            // ========== NESTING PANEL ==========
            RibbonPanelSource nestingPanelSource = new RibbonPanelSource();
            nestingPanelSource.Title = "Nesting";

            RibbonPanel nestingPanel = new RibbonPanel();
            nestingPanel.Source = nestingPanelSource;
            tab.Panels.Add(nestingPanel);

            nestingPanelSource.Items.Add(CreateLargeButton("Create\nNest", "MC_CREATE_NEST"));
            nestingPanelSource.Items.Add(CreateLargeButton("Quick\nNest", "MC_QUICK_NEST"));

            // ========== TOOLS PANEL ==========
            RibbonPanelSource toolsPanelSource = new RibbonPanelSource();
            toolsPanelSource.Title = "Tools";

            RibbonPanel toolsPanel = new RibbonPanel();
            toolsPanel.Source = toolsPanelSource;
            tab.Panels.Add(toolsPanel);

            toolsPanelSource.Items.Add(CreateLargeButton("Part\nProperties", "MC_SHOW_METADATA_PALETTE"));
            toolsPanelSource.Items.Add(CreateLargeButton("Database\nConfig", "MC_CONFIG_DATABASE"));
        }

        /// <summary>
        /// Helper method to create a large ribbon button.
        /// </summary>
        private RibbonButton CreateLargeButton(string text, string command)
        {
            RibbonButton button = new RibbonButton();
            button.Text = text;
            button.ShowText = true;
            button.Size = RibbonItemSize.Large;
            button.Orientation = System.Windows.Controls.Orientation.Vertical;
            button.CommandHandler = new RibbonCommandHandler();
            button.CommandParameter = command;
            return button;
        }

        /// <summary>
        /// Called when the plugin is unloaded from AutoCAD.
        /// </summary>
        public void Terminate()
        {
            // Cleanup
        }
    }

    /// <summary>
    /// Handles button clicks on the ribbon.
    /// </summary>
    public class RibbonCommandHandler : System.Windows.Input.ICommand
    {
        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            string commandName = parameter as string;

            if (string.IsNullOrEmpty(commandName))
                return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;

            if (doc == null)
                return;

            try
            {
                // Use simple command execution with leading underscore
                // The space at the end acts as Enter to execute the command
                doc.SendStringToExecute(commandName + "\n", true, false, true);
            }
            catch (System.Exception ex)
            {
                try
                {
                    doc.Editor.WriteMessage($"\nError executing {commandName}: {ex.Message}");
                }
                catch { }
            }
        }
    }
}
