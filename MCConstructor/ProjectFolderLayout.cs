using System.Collections.Generic;
using System.IO;

namespace MCConstructor
{
    /// <summary>
    /// Shared definition of the standard MC Constructor project folder layout.
    /// CreateProject creates these directories and CreateDrawing routes new
    /// .dwg files into the matching subfolder.
    /// </summary>
    public static class ProjectFolderLayout
    {
        public const string Admin           = @"00 Admin";
        public const string Standards       = @"01 Standards";
        public const string Templates       = @"01 Standards\Templates";
        public const string Titleblocks     = @"01 Standards\Titleblocks";
        public const string Blocks          = @"01 Standards\Blocks";
        public const string Profiles        = @"01 Standards\Profiles";
        public const string Models          = @"02 Models";
        public const string ModelsGeneral   = @"02 Models\General";
        public const string ModelsPiping    = @"02 Models\Piping";
        public const string ModelsElectrical= @"02 Models\Electrical";
        public const string ModelsHVAC      = @"02 Models\HVAC";
        public const string Sheets          = @"03 Sheets";
        public const string Fabrication     = @"03 Fabrication";
        public const string ProfilePlots    = @"03 Fabrication\Profile Plots";
        public const string Output          = @"04 Output";

        /// <summary>
        /// All folders that should exist under a project root, in creation
        /// order (parents before children, though Directory.CreateDirectory
        /// is happy either way).
        /// </summary>
        public static readonly IReadOnlyList<string> AllFolders = new[]
        {
            Admin,
            Standards,
            Templates,
            Titleblocks,
            Blocks,
            Profiles,
            Models,
            ModelsGeneral,
            ModelsPiping,
            ModelsElectrical,
            ModelsHVAC,
            Sheets,
            Fabrication,
            ProfilePlots,
            Output,
        };

        /// <summary>
        /// Create the full folder structure under <paramref name="projectRoot"/>.
        /// Idempotent: existing folders are left alone.
        /// </summary>
        public static void CreateLayout(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new System.ArgumentException("projectRoot is required.", nameof(projectRoot));

            Directory.CreateDirectory(projectRoot);
            foreach (var sub in AllFolders)
            {
                Directory.CreateDirectory(Path.Combine(projectRoot, sub));
            }
        }
    }
}
