using System;
using System.Collections.Generic;

namespace FirstAcadPlugin
{
    public class Project
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        /// <summary>
        /// Absolute root directory on the local file system (e.g. C:\Projects\MyProject).
        /// The standard "00 Admin", "01 Standards", ... folders live under this path.
        /// </summary>
        public string Directory { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>
    /// A drawing belonging to a project. Mirrors public.drawings.
    /// </summary>
    public class Drawing
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        /// <summary>One of <see cref="DrawingTypes"/>.</summary>
        public string DrawingType { get; set; }
        /// <summary>One of <see cref="DrawingDisciplines"/>, or null.</summary>
        public string Discipline { get; set; }
        public string FilePath { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>
    /// Allowed values for Drawing.DrawingType. Keep in sync with the CHECK
    /// constraint in SQL/create_drawings_table.sql.
    /// </summary>
    public static class DrawingTypes
    {
        public const string BlockLibrary = "BlockLibrary";
        public const string FunctionalDrawing = "FunctionalDrawing";
        public const string DetailDrawing = "DetailDrawing";
        public const string Template = "Template";
        public const string Titleblock = "Titleblock";
        public const string Sheet = "Sheet";
        public const string Profile = "Profile";
        public const string ProfilePlot = "ProfilePlot";
        public const string Other = "Other";

        public static readonly string[] All = {
            BlockLibrary, FunctionalDrawing, DetailDrawing,
            Template, Titleblock, Sheet, Profile, ProfilePlot, Other
        };

        /// <summary>True for types that need a discipline.</summary>
        public static bool RequiresDiscipline(string drawingType)
        {
            return drawingType == FunctionalDrawing || drawingType == DetailDrawing;
        }

        /// <summary>
        /// The relative folder under the project root where a drawing of this
        /// type lives. Discipline is appended for functional drawings.
        /// </summary>
        public static string RelativeFolder(string drawingType, string discipline)
        {
            switch (drawingType)
            {
                case BlockLibrary:      return @"01 Standards\Blocks";
                case Template:          return @"01 Standards\Templates";
                case Titleblock:        return @"01 Standards\Titleblocks";
                case FunctionalDrawing:
                case DetailDrawing:     return System.IO.Path.Combine(@"02 Models", discipline ?? DrawingDisciplines.General);
                case Sheet:             return @"03 Sheets";
                case Profile:           return @"01 Standards\Profiles";
                case ProfilePlot:       return @"03 Fabrication\Profile Plots";
                case Other:             return @"00 Admin";
                default:                return @"00 Admin";
            }
        }

        /// <summary>Friendly label for combo boxes.</summary>
        public static string DisplayName(string drawingType)
        {
            switch (drawingType)
            {
                case BlockLibrary:      return "Block Library";
                case FunctionalDrawing: return "Functional Drawing";
                case DetailDrawing:     return "Detail Drawing";
                case Template:          return "Template";
                case Titleblock:        return "Titleblock";
                case Sheet:             return "Sheet";
                case Profile:           return "Profile (Cross-Section)";
                case ProfilePlot:       return "Profile Plot";
                case Other:             return "Other";
                default:                return drawingType;
            }
        }
    }

    /// <summary>
    /// Allowed values for Drawing.Discipline. Keep in sync with SQL CHECK constraint.
    /// </summary>
    public static class DrawingDisciplines
    {
        public const string General = "General";
        public const string Piping = "Piping";
        public const string Electrical = "Electrical";
        public const string HVAC = "HVAC";

        public static readonly string[] All = { General, Piping, Electrical, HVAC };
    }

    /// <summary>
    /// A steel grade / material spec used by nesting and profiles.
    /// Mirrors public.materials. Density is stored in kg/m^3.
    /// </summary>
    public class Material
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        /// <summary>kg/m^3. Default 7850 (mild steel).</summary>
        public double Density { get; set; } = 7850;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>
    /// A stock plate size belonging to a material. Mirrors
    /// public.material_plates. Dimensions in mm, matching the rest of the
    /// nesting code.
    /// </summary>
    public class MaterialPlate
    {
        public Guid Id { get; set; }
        public Guid MaterialId { get; set; }
        /// <summary>Short code shown in the nesting picker, e.g. "T04".</summary>
        public string Code { get; set; }
        public double Width { get; set; }      // mm
        public double Height { get; set; }     // mm
        public double Thickness { get; set; }  // mm
        public string Description { get; set; }
        /// <summary>True for the plate the nesting dialog should pre-select.</summary>
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Material info denormalized for display in the nesting picker.
        // Populated by GetPlatesForCurrentProject. Not persisted.
        public string MaterialName { get; set; }
        public double MaterialDensity { get; set; }

        /// <summary>
        /// Label shown in nesting / list pickers, e.g.
        /// "T04 (DH-36, 2438 x 1219 x 10 mm)".
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                string mat = string.IsNullOrEmpty(MaterialName) ? "?" : MaterialName;
                return $"{Code} ({mat}, {Width:0.#} x {Height:0.#} x {Thickness:0.#} mm)";
            }
        }

        public override string ToString() => DisplayLabel;
    }

    /// <summary>
    /// A profile (structural T, bulb flat, angle, ...) defined by a 2D
    /// cross-section .dwg under 01 Standards/Profiles plus a material.
    /// Mirrors public.profiles.
    ///
    /// At insert time, the cross-section drawing's modelspace polyline is
    /// extruded to the user-specified length to produce a 3D solid.
    /// </summary>
    public class Profile
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public Guid? MaterialId { get; set; }
        public Guid? DrawingId { get; set; }
        /// <summary>Optional override; falls back to material's density when null.</summary>
        public double? DensityOverride { get; set; }
        /// <summary>mm^2 - cached cross-section area, computed by the plugin.</summary>
        public double? CrossSectionArea { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Joined lookups (display only, not persisted directly here).
        public string MaterialName { get; set; }
        public double? MaterialDensity { get; set; }
        public string DrawingFilePath { get; set; }
        public string DrawingName { get; set; }

        /// <summary>
        /// Effective density (kg/m^3) used for weight calcs: override wins,
        /// otherwise the material's density. Returns null when neither is set.
        /// </summary>
        public double? EffectiveDensity =>
            DensityOverride ?? MaterialDensity;

        public override string ToString() => Name;
    }

    /// <summary>
    /// Complete part data for storage and recreation.
    /// </summary>
    public class DrawingPart
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }

        // Part identification
        public string PartName { get; set; }
        public string PartDescription { get; set; }

        // Source tracking
        public string SourceDrawingName { get; set; }
        public string SourceObjectHandle { get; set; }

        // Reference tracking
        public Guid? ParentPartId { get; set; }
        public bool IsOriginal { get; set; } = true;
        public bool IsOverride { get; set; } = false;

        // Geometry (SAT format)
        public string GeometryData { get; set; }
        public string GeometryType { get; set; } = "3DSOLID";

        // Transform
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double PositionZ { get; set; }
        public double RotationX { get; set; }
        public double RotationY { get; set; }
        public double RotationZ { get; set; }
        public double ScaleX { get; set; } = 1;
        public double ScaleY { get; set; } = 1;
        public double ScaleZ { get; set; } = 1;

        // Visual properties
        public string LayerName { get; set; } = "0";
        public int ColorIndex { get; set; } = 256;
        public string MaterialName { get; set; }

        // Bounding box
        public double? BBoxMinX { get; set; }
        public double? BBoxMinY { get; set; }
        public double? BBoxMinZ { get; set; }
        public double? BBoxMaxX { get; set; }
        public double? BBoxMaxY { get; set; }
        public double? BBoxMaxZ { get; set; }

        // Material link (FK → public.materials.id)
        public Guid? MaterialId { get; set; }

        // Metadata
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int Version { get; set; } = 1;
    }

    // ========================================================================
    // DatabaseService — facade that delegates to the active IStorageProvider
    // ========================================================================

    public static class DatabaseService
    {
        public static Project CurrentProject { get; private set; }

        public const string NestTemplateName = "NEST TEMPLATE";

        // ----------------------------------------------------------------
        // Connection string management — routes to StorageRouter
        // ----------------------------------------------------------------

        public static void SetConnectionString(string connectionString)
        {
            StorageRouter.UseNpgsql(connectionString);
        }

        public static void SetConnectionString(string host, int port, string database, string username, string password)
        {
            string cs = $"Host={host};Port={port};Database={database};Username={username};Password={password}";
            StorageRouter.UseNpgsql(cs);
        }

        // Kept for ABI compatibility; used by JsonMigrationService and ProjectFileWriter.
        internal static string GetConnectionString() => StorageRouter.GetNpgsqlConnectionString();

        public static void SetCurrentProject(Project project) => CurrentProject = project;

        public static bool TestConnection(out string errorMessage)
        {
            if (!StorageRouter.IsInitialized)
            {
                errorMessage = "No storage provider initialized.";
                return false;
            }
            return StorageRouter.Provider.TestConnection(out errorMessage);
        }

        // ----------------------------------------------------------------
        // Projects
        // ----------------------------------------------------------------

        public static List<Project> GetProjects() => StorageRouter.Provider.GetProjects();

        public static Project CreateProject(string name, string description, string directory)
        {
            var project = StorageRouter.Provider.CreateProject(name, description, directory);
            CurrentProject = project;
            return project;
        }

        public static bool ProjectNameExists(string name) => StorageRouter.Provider.ProjectNameExists(name);

        // ----------------------------------------------------------------
        // Drawings
        // ----------------------------------------------------------------

        public static Drawing CreateDrawing(string name, string drawingType, string discipline, string filePath, string description = null)
        {
            return StorageRouter.Provider.CreateDrawing(Guid.Empty, name, drawingType, discipline, filePath, description);
        }

        public static Drawing CreateDrawing(Guid id, string name, string drawingType, string discipline, string filePath, string description = null)
        {
            return StorageRouter.Provider.CreateDrawing(id, name, drawingType, discipline, filePath, description);
        }

        public static bool DrawingNameExistsInCurrentProject(string name) => StorageRouter.Provider.DrawingNameExistsInCurrentProject(name);

        public static List<Drawing> GetDrawings() => StorageRouter.Provider.GetDrawings();

        public static Drawing FindNestTemplate() => StorageRouter.Provider.FindNestTemplate();

        public static Drawing GetDrawingByPath(string filePath) => StorageRouter.Provider.GetDrawingByPath(filePath);

        public static void UpdateDrawingProperties(Guid drawingId, string drawingType, string discipline, string description)
            => StorageRouter.Provider.UpdateDrawingProperties(drawingId, drawingType, discipline, description);

        // ----------------------------------------------------------------
        // Parts
        // ----------------------------------------------------------------

        public static Guid SavePart(DrawingPart part) => StorageRouter.Provider.SavePart(part);

        public static List<DrawingPart> GetOriginalParts() => StorageRouter.Provider.GetOriginalParts();

        public static DrawingPart GetPartById(Guid partId) => StorageRouter.Provider.GetPartById(partId);

        public static List<DrawingPart> GetPartReferences(Guid parentPartId) => StorageRouter.Provider.GetPartReferences(parentPartId);

        public static List<DrawingPart> GetOverriddenParts() => StorageRouter.Provider.GetOverriddenParts();

        public static void OverridePart(Guid partId, DrawingPart updatedPart) => StorageRouter.Provider.OverridePart(partId, updatedPart);

        public static void ApplyOverrideToOriginal(Guid overridePartId) => StorageRouter.Provider.ApplyOverrideToOriginal(overridePartId);

        public static Guid SavePartReference(DrawingPart part, Guid parentPartId) => StorageRouter.Provider.SavePartReference(part, parentPartId);
    }
}
