using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;

// Alias to avoid conflicts with System.Windows.Application (WPF).
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Plugin entry point. Builds the "MC Constructor" ribbon tab with
    /// command-fired buttons.
    /// </summary>
    public class MyPlugin : IExtensionApplication
    {
        // Stable Id for the tab. We use it to FindTab+Remove on rebuild so
        // NETLOAD'ing a fresh build over the previous one doesn't leave a
        // dead tab pointing at unloaded handlers.
        private const string TabId = "MC_CONSTRUCTOR_TAB";

        // Per-category background color used when generating fallback icons.
        // Drop a real PNG at <dll>/Images/<CommandName>.png to override.
        private static readonly Color ColProject  = Color.FromRgb(  0, 120, 200); // blue
        private static readonly Color ColParts    = Color.FromRgb( 40, 160,  80); // green
        private static readonly Color ColRefs     = Color.FromRgb(220, 130,  40); // orange
        private static readonly Color ColNesting  = Color.FromRgb(140,  80, 180); // purple
        private static readonly Color ColTools    = Color.FromRgb( 90,  90,  95); // slate

        // Single shared command handler so each click goes through the same
        // SendStringToExecute path; per-button instances are unnecessary.
        private static readonly RibbonCommandHandler CommandHandler = new RibbonCommandHandler();

        private static bool _assemblyResolverRegistered = false;

        public void Initialize()
        {
            if (!_assemblyResolverRegistered)
            {
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                _assemblyResolverRegistered = true;
            }

            // Wait for AutoCAD to be ready before touching the ribbon.
            AcadApp.Idle += OnApplicationIdle;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string assemblyPath = Path.Combine(pluginDir, assemblyName.Name + ".dll");
            if (File.Exists(assemblyPath))
            {
                try { return Assembly.LoadFrom(assemblyPath); }
                catch { }
            }
            return null;
        }

        private void OnApplicationIdle(object sender, EventArgs e)
        {
            AcadApp.Idle -= OnApplicationIdle;
            CreateRibbon();
            MetadataPalette.Initialize();
        }

        /// <summary>
        /// Build (or rebuild) the MC Constructor tab.
        /// We FindTab + Remove first so a NETLOAD over a previous load
        /// doesn't leave the old tab in place pointing at the now-unloaded
        /// command handlers (the classic "buttons stop working" bug).
        /// </summary>
        private void CreateRibbon()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            // Drop any existing tab from a prior load before adding the new one.
            var existing = ribbon.FindTab(TabId);
            if (existing != null)
                ribbon.Tabs.Remove(existing);

            var tab = new RibbonTab
            {
                Title = "MC Constructor",
                Id = TabId
            };
            ribbon.Tabs.Add(tab);

            // ---------- PROJECT ----------
            var projectPanel = MakePanel("Project");
            projectPanel.Source.Items.Add(MakeButton("Create\nProject",  "MCCreateProject",  ColProject));
            projectPanel.Source.Items.Add(MakeButton("Open\nProject",    "MCOpenProject",    ColProject));
            projectPanel.Source.Items.Add(MakeButton("Navigator",        "MCNavigator",      ColProject));
            projectPanel.Source.Items.Add(MakeButton("New\nDrawing",     "MCCreateDrawing",  ColProject));
            projectPanel.Source.Items.Add(MakeButton("Save\nParts",      "MCSaveParts",      ColProject));
            projectPanel.Source.Items.Add(MakeButton("Project\nStatus",  "MCProjectStatus",  ColProject));
            tab.Panels.Add(projectPanel.Panel);

            // ---------- PARTS ----------
            var partsPanel = MakePanel("Parts");
            partsPanel.Source.Items.Add(MakeButton("Add\nMetadata",   "MCAddPartMetadata",   ColParts));
            partsPanel.Source.Items.Add(MakeButton("Batch\nName",     "MCBatchAddPartNames", ColParts));
            partsPanel.Source.Items.Add(MakeButton("View\nMetadata",  "MCViewPartMetadata",  ColParts));
            partsPanel.Source.Items.Add(MakeButton("List\nParts",     "MCListParts",         ColParts));
            tab.Panels.Add(partsPanel.Panel);

            // ---------- REFERENCES ----------
            var refsPanel = MakePanel("References");
            refsPanel.Source.Items.Add(MakeButton("Insert\nPart",       "MCInsertPart",      ColRefs));
            refsPanel.Source.Items.Add(MakeButton("Override\nPart",     "MCOverridePart",    ColRefs));
            refsPanel.Source.Items.Add(MakeButton("Update\nAll Parts",  "MCUpdateAllParts",  ColRefs));
            refsPanel.Source.Items.Add(MakeButton("List DB\nParts",     "MCListDbParts",     ColRefs));
            tab.Panels.Add(refsPanel.Panel);

            // ---------- NESTING ----------
            var nestingPanel = MakePanel("Nesting");
            nestingPanel.Source.Items.Add(MakeButton("Create\nNest", "MCCreateNest", ColNesting));
            nestingPanel.Source.Items.Add(MakeButton("Quick\nNest",  "MCQuickNest",  ColNesting));
            tab.Panels.Add(nestingPanel.Panel);

            // ---------- TOOLS ----------
            var toolsPanel = MakePanel("Tools");
            toolsPanel.Source.Items.Add(MakeButton("Part\nProperties",    "MCShowMetadataPalette", ColTools));
            toolsPanel.Source.Items.Add(MakeButton("Drawing\nProperties", "MCDrawingProperties",   ColTools));
            toolsPanel.Source.Items.Add(MakeButton("Material\nLibrary",   "MCMaterialLibrary",     ColTools));
            toolsPanel.Source.Items.Add(MakeButton("Database\nConfig",    "MCConfigDatabase",      ColTools));
            tab.Panels.Add(toolsPanel.Panel);
        }

        private static (RibbonPanel Panel, RibbonPanelSource Source) MakePanel(string title)
        {
            var src = new RibbonPanelSource { Title = title };
            var panel = new RibbonPanel { Source = src };
            return (panel, src);
        }

        /// <summary>
        /// Create a large vertical button wired to <paramref name="commandName"/>.
        /// Both LargeImage (32px) and Image (16px) are populated so the button
        /// looks right whether the panel is rendered large or compact.
        /// </summary>
        private static RibbonButton MakeButton(string text, string commandName, Color fallbackColor)
        {
            var button = new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                CommandHandler = CommandHandler,
                CommandParameter = commandName,
                LargeImage = RibbonIcons.Load(commandName, text, fallbackColor, 32),
                Image      = RibbonIcons.Load(commandName, text, fallbackColor, 16),
            };
            return button;
        }

        public void Terminate() { /* nothing to clean up */ }
    }

    /// <summary>
    /// Bridges a ribbon button click to the AutoCAD command processor.
    /// CommandParameter is the registered command name (e.g. "MCCreateProject").
    /// </summary>
    public class RibbonCommandHandler : System.Windows.Input.ICommand
    {
        // Required by ICommand. We never need to raise this because all our
        // buttons are always enabled.
        public event EventHandler CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            string commandName = parameter as string;
            if (string.IsNullOrEmpty(commandName)) return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                // Trailing space acts as Enter for SendStringToExecute. We
                // deliberately use space instead of "\n" because the latter
                // can be interpreted twice on some locales.
                doc.SendStringToExecute(commandName + " ", true, false, true);
            }
            catch (System.Exception ex)
            {
                try { doc.Editor.WriteMessage($"\nError executing {commandName}: {ex.Message}"); }
                catch { }
            }
        }
    }
}
