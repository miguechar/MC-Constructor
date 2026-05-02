using Npgsql;
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

        /// <summary>True for types that need a discipline (i.e. functional drawings).</summary>
        public static bool RequiresDiscipline(string drawingType)
        {
            return drawingType == FunctionalDrawing;
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
                case FunctionalDrawing: return System.IO.Path.Combine(@"02 Models", discipline ?? DrawingDisciplines.General);
                case DetailDrawing:     return @"02 Models\General";
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

    public static class DatabaseService
    {
        private static string _connectionString = "Host=localhost;Port=5432;Database=your_database;Username=postgres;Password=your_password";

        public static Project CurrentProject { get; private set; }

        public static void SetConnectionString(string connectionString) => _connectionString = connectionString;

        /// <summary>
        /// Read-only access to the Npgsql connection string for sister
        /// services (MaterialLibraryService) that need to open their own
        /// connections. Stays internal to the assembly.
        /// </summary>
        internal static string GetConnectionString() => _connectionString;

        public static void SetConnectionString(string host, int port, string database, string username, string password)
        {
            _connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password}";
        }

        public static bool TestConnection(out string errorMessage)
        {
            errorMessage = null;
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static List<Project> GetProjects()
        {
            var projects = new List<Project>();
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    "SELECT id, name, description, directory FROM public.projects ORDER BY name", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        projects.Add(new Project
                        {
                            Id = reader.GetGuid(0),
                            Name = reader.GetString(1),
                            Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Directory = reader.IsDBNull(3) ? "" : reader.GetString(3)
                        });
                    }
                }
            }
            return projects;
        }

        public static void SetCurrentProject(Project project) => CurrentProject = project;

        // ====================================================================
        // PROJECT CREATION
        // ====================================================================

        /// <summary>
        /// Insert a new project row. Returns the populated Project (with Id
        /// assigned by the database). Does NOT create folders on disk - that
        /// is handled by the caller (see CreateProjectCommand).
        /// </summary>
        public static Project CreateProject(string name, string description, string directory)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Project name is required.", nameof(name));
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Project directory is required.", nameof(directory));

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    @"INSERT INTO public.projects (name, description, directory)
                      VALUES (@name, @description, @directory)
                      RETURNING id", conn))
                {
                    cmd.Parameters.AddWithValue("name", name);
                    cmd.Parameters.AddWithValue("description", (object)description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("directory", directory);

                    var id = (Guid)cmd.ExecuteScalar();
                    return new Project
                    {
                        Id = id,
                        Name = name,
                        Description = description,
                        Directory = directory
                    };
                }
            }
        }

        /// <summary>True if a project with the given name already exists.</summary>
        public static bool ProjectNameExists(string name)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    "SELECT 1 FROM public.projects WHERE name = @name LIMIT 1", conn))
                {
                    cmd.Parameters.AddWithValue("name", name);
                    return cmd.ExecuteScalar() != null;
                }
            }
        }

        // ====================================================================
        // DRAWINGS
        // ====================================================================

        /// <summary>
        /// Insert a new drawing row for the current project. The .dwg file at
        /// filePath should already have been written by the caller.
        /// </summary>
        public static Drawing CreateDrawing(string name, string drawingType, string discipline, string filePath, string description = null)
        {
            // Let the database generate the id.
            return CreateDrawing(Guid.Empty, name, drawingType, discipline, filePath, description);
        }

        /// <summary>
        /// Insert a new drawing row using an explicit id. Pass <see cref="Guid.Empty"/>
        /// to fall back to the database default. Useful when the caller
        /// needs to stamp the id into the .dwg file before opening it (so we
        /// avoid having to SaveAs the active document, which would throw
        /// eNotApplicable).
        /// </summary>
        public static Drawing CreateDrawing(Guid id, string name, string drawingType, string discipline, string filePath, string description = null)
        {
            if (CurrentProject == null)
                throw new InvalidOperationException("No project open. Use MCOpenProject first.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Drawing name is required.", nameof(name));
            if (string.IsNullOrWhiteSpace(drawingType))
                throw new ArgumentException("Drawing type is required.", nameof(drawingType));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            bool useExplicitId = id != Guid.Empty;

            string sql = useExplicitId
                ? @"INSERT INTO public.drawings
                        (id, project_id, name, description, drawing_type, discipline, file_path)
                      VALUES (@id, @projectId, @name, @description, @drawingType, @discipline, @filePath)
                      RETURNING id, created_at, updated_at"
                : @"INSERT INTO public.drawings
                        (project_id, name, description, drawing_type, discipline, file_path)
                      VALUES (@projectId, @name, @description, @drawingType, @discipline, @filePath)
                      RETURNING id, created_at, updated_at";

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    if (useExplicitId)
                        cmd.Parameters.AddWithValue("id", id);
                    cmd.Parameters.AddWithValue("projectId", CurrentProject.Id);
                    cmd.Parameters.AddWithValue("name", name);
                    cmd.Parameters.AddWithValue("description", (object)description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("drawingType", drawingType);
                    cmd.Parameters.AddWithValue("discipline", (object)discipline ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("filePath", filePath);

                    using (var r = cmd.ExecuteReader())
                    {
                        r.Read();
                        return new Drawing
                        {
                            Id = r.GetGuid(0),
                            ProjectId = CurrentProject.Id,
                            Name = name,
                            Description = description,
                            DrawingType = drawingType,
                            Discipline = discipline,
                            FilePath = filePath,
                            CreatedAt = r.GetDateTime(1),
                            UpdatedAt = r.GetDateTime(2)
                        };
                    }
                }
            }
        }

        /// <summary>True if a drawing with this name already exists in the current project.</summary>
        public static bool DrawingNameExistsInCurrentProject(string name)
        {
            if (CurrentProject == null) return false;
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    "SELECT 1 FROM public.drawings WHERE project_id = @projectId AND name = @name LIMIT 1", conn))
                {
                    cmd.Parameters.AddWithValue("projectId", CurrentProject.Id);
                    cmd.Parameters.AddWithValue("name", name);
                    return cmd.ExecuteScalar() != null;
                }
            }
        }

        /// <summary>All drawings registered for the current project.</summary>
        public static List<Drawing> GetDrawings()
        {
            var drawings = new List<Drawing>();
            if (CurrentProject == null) return drawings;

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    @"SELECT id, project_id, name, description, drawing_type, discipline,
                             file_path, created_at, updated_at
                      FROM public.drawings
                      WHERE project_id = @projectId
                      ORDER BY name", conn))
                {
                    cmd.Parameters.AddWithValue("projectId", CurrentProject.Id);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            drawings.Add(new Drawing
                            {
                                Id = r.GetGuid(0),
                                ProjectId = r.GetGuid(1),
                                Name = r.GetString(2),
                                Description = r.IsDBNull(3) ? null : r.GetString(3),
                                DrawingType = r.GetString(4),
                                Discipline = r.IsDBNull(5) ? null : r.GetString(5),
                                FilePath = r.GetString(6),
                                CreatedAt = r.GetDateTime(7),
                                UpdatedAt = r.GetDateTime(8)
                            });
                        }
                    }
                }
            }
            return drawings;
        }

        /// <summary>
        /// Conventional name for the template that supplies the title block
        /// to nest output drawings. The user creates this via MCCreateDrawing
        /// (drawing_type=Template, name=NEST TEMPLATE) and saves their
        /// titleblock layout in it.
        /// </summary>
        public const string NestTemplateName = "NEST TEMPLATE";

        /// <summary>
        /// Find the project's nest template, if present. Match is
        /// case-insensitive on name and looks only at drawings of type
        /// 'Template'. Returns null when the current project has none, or
        /// when no project is open.
        /// </summary>
        public static Drawing FindNestTemplate()
        {
            if (CurrentProject == null) return null;

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    @"SELECT id, project_id, name, description, drawing_type, discipline,
                             file_path, created_at, updated_at
                      FROM public.drawings
                      WHERE project_id = @projectId
                        AND drawing_type = 'Template'
                        AND UPPER(name) = @name
                      LIMIT 1", conn))
                {
                    cmd.Parameters.AddWithValue("projectId", CurrentProject.Id);
                    cmd.Parameters.AddWithValue("name", NestTemplateName.ToUpperInvariant());

                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return null;
                        return new Drawing
                        {
                            Id = r.GetGuid(0),
                            ProjectId = r.GetGuid(1),
                            Name = r.GetString(2),
                            Description = r.IsDBNull(3) ? null : r.GetString(3),
                            DrawingType = r.GetString(4),
                            Discipline = r.IsDBNull(5) ? null : r.GetString(5),
                            FilePath = r.GetString(6),
                            CreatedAt = r.GetDateTime(7),
                            UpdatedAt = r.GetDateTime(8)
                        };
                    }
                }
            }
        }

        /// <summary>
        /// Look up a drawing by its absolute file path. Returns null if not
        /// found. Useful when the user opens a .dwg from disk and we need to
        /// reattach it to its DB row.
        /// </summary>
        public static Drawing GetDrawingByPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    @"SELECT id, project_id, name, description, drawing_type, discipline,
                             file_path, created_at, updated_at
                      FROM public.drawings
                      WHERE file_path = @filePath LIMIT 1", conn))
                {
                    cmd.Parameters.AddWithValue("filePath", filePath);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return null;
                        return new Drawing
                        {
                            Id = r.GetGuid(0),
                            ProjectId = r.GetGuid(1),
                            Name = r.GetString(2),
                            Description = r.IsDBNull(3) ? null : r.GetString(3),
                            DrawingType = r.GetString(4),
                            Discipline = r.IsDBNull(5) ? null : r.GetString(5),
                            FilePath = r.GetString(6),
                            CreatedAt = r.GetDateTime(7),
                            UpdatedAt = r.GetDateTime(8)
                        };
                    }
                }
            }
        }

        /// <summary>Update the type/discipline/description on an existing drawing row.</summary>
        public static void UpdateDrawingProperties(Guid drawingId, string drawingType, string discipline, string description)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    @"UPDATE public.drawings
                      SET drawing_type = @drawingType,
                          discipline   = @discipline,
                          description  = @description
                      WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", drawingId);
                    cmd.Parameters.AddWithValue("drawingType", drawingType);
                    cmd.Parameters.AddWithValue("discipline", (object)discipline ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("description", (object)description ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Save a complete part with all geometry data.
        /// </summary>
        public static Guid SavePart(DrawingPart part)
        {
            if (CurrentProject == null)
                throw new InvalidOperationException("No project open. Use MCOpenProject first.");

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                // Check if part exists by source handle
                using (var checkCmd = new NpgsqlCommand(
                    @"SELECT id FROM public.drawing_parts
                      WHERE project_id = @projectId
                      AND source_drawing_name = @sourceDrawing
                      AND source_object_handle = @sourceHandle", conn))
                {
                    checkCmd.Parameters.AddWithValue("projectId", CurrentProject.Id);
                    checkCmd.Parameters.AddWithValue("sourceDrawing", part.SourceDrawingName);
                    checkCmd.Parameters.AddWithValue("sourceHandle", part.SourceObjectHandle);

                    var existingId = checkCmd.ExecuteScalar();

                    if (existingId != null && existingId != DBNull.Value)
                    {
                        // Update existing
                        return UpdatePart((Guid)existingId, part, conn);
                    }
                }

                // Insert new
                using (var cmd = new NpgsqlCommand(
                    @"INSERT INTO public.drawing_parts (
                        project_id, part_name, part_description,
                        source_drawing_name, source_object_handle,
                        parent_part_id, is_original, is_override,
                        geometry_data, geometry_type,
                        position_x, position_y, position_z,
                        rotation_x, rotation_y, rotation_z,
                        scale_x, scale_y, scale_z,
                        layer_name, color_index, material_name,
                        bbox_min_x, bbox_min_y, bbox_min_z,
                        bbox_max_x, bbox_max_y, bbox_max_z,
                        material_id
                    ) VALUES (
                        @projectId, @partName, @partDesc,
                        @sourceDrawing, @sourceHandle,
                        @parentPartId, @isOriginal, @isOverride,
                        @geometryData, @geometryType,
                        @posX, @posY, @posZ,
                        @rotX, @rotY, @rotZ,
                        @scaleX, @scaleY, @scaleZ,
                        @layerName, @colorIndex, @materialName,
                        @bboxMinX, @bboxMinY, @bboxMinZ,
                        @bboxMaxX, @bboxMaxY, @bboxMaxZ,
                        @materialId
                    ) RETURNING id", conn))
                {
                    AddPartParameters(cmd, part);
                    return (Guid)cmd.ExecuteScalar();
                }
            }
        }

        private static Guid UpdatePart(Guid id, DrawingPart part, NpgsqlConnection conn)
        {
            using (var cmd = new NpgsqlCommand(
                @"UPDATE public.drawing_parts SET
                    part_name = @partName, part_description = @partDesc,
                    geometry_data = @geometryData, geometry_type = @geometryType,
                    position_x = @posX, position_y = @posY, position_z = @posZ,
                    rotation_x = @rotX, rotation_y = @rotY, rotation_z = @rotZ,
                    scale_x = @scaleX, scale_y = @scaleY, scale_z = @scaleZ,
                    layer_name = @layerName, color_index = @colorIndex, material_name = @materialName,
                    bbox_min_x = @bboxMinX, bbox_min_y = @bboxMinY, bbox_min_z = @bboxMinZ,
                    bbox_max_x = @bboxMaxX, bbox_max_y = @bboxMaxY, bbox_max_z = @bboxMaxZ,
                    is_override = @isOverride, material_id = @materialId
                WHERE id = @id RETURNING id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                AddPartParameters(cmd, part);
                return (Guid)cmd.ExecuteScalar();
            }
        }

        private static void AddPartParameters(NpgsqlCommand cmd, DrawingPart part)
        {
            cmd.Parameters.AddWithValue("projectId", CurrentProject.Id);
            cmd.Parameters.AddWithValue("partName", part.PartName ?? "");
            cmd.Parameters.AddWithValue("partDesc", (object)part.PartDescription ?? DBNull.Value);
            cmd.Parameters.AddWithValue("sourceDrawing", part.SourceDrawingName);
            cmd.Parameters.AddWithValue("sourceHandle", part.SourceObjectHandle);
            cmd.Parameters.AddWithValue("parentPartId", (object)part.ParentPartId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("isOriginal", part.IsOriginal);
            cmd.Parameters.AddWithValue("isOverride", part.IsOverride);
            cmd.Parameters.AddWithValue("geometryData", (object)part.GeometryData ?? DBNull.Value);
            cmd.Parameters.AddWithValue("geometryType", part.GeometryType ?? "3DSOLID");
            cmd.Parameters.AddWithValue("posX", part.PositionX);
            cmd.Parameters.AddWithValue("posY", part.PositionY);
            cmd.Parameters.AddWithValue("posZ", part.PositionZ);
            cmd.Parameters.AddWithValue("rotX", part.RotationX);
            cmd.Parameters.AddWithValue("rotY", part.RotationY);
            cmd.Parameters.AddWithValue("rotZ", part.RotationZ);
            cmd.Parameters.AddWithValue("scaleX", part.ScaleX);
            cmd.Parameters.AddWithValue("scaleY", part.ScaleY);
            cmd.Parameters.AddWithValue("scaleZ", part.ScaleZ);
            cmd.Parameters.AddWithValue("layerName", part.LayerName ?? "0");
            cmd.Parameters.AddWithValue("colorIndex", part.ColorIndex);
            cmd.Parameters.AddWithValue("materialName", (object)part.MaterialName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("bboxMinX", (object)part.BBoxMinX ?? DBNull.Value);
            cmd.Parameters.AddWithValue("bboxMinY", (object)part.BBoxMinY ?? DBNull.Value);
            cmd.Parameters.AddWithValue("bboxMinZ", (object)part.BBoxMinZ ?? DBNull.Value);
            cmd.Parameters.AddWithValue("bboxMaxX", (object)part.BBoxMaxX ?? DBNull.Value);
            cmd.Parameters.AddWithValue("bboxMaxY", (object)part.BBoxMaxY ?? DBNull.Value);
            cmd.Parameters.AddWithValue("bboxMaxZ", (object)part.BBoxMaxZ ?? DBNull.Value);
            cmd.Parameters.AddWithValue("materialId", (object)part.MaterialId ?? DBNull.Value);
        }

        /// <summary>
        /// Get all original parts for the current project.
        /// </summary>
        public static List<DrawingPart> GetOriginalParts()
        {
            if (CurrentProject == null) return new List<DrawingPart>();
            return GetParts("WHERE project_id = @projectId AND is_original = TRUE ORDER BY part_name");
        }

        /// <summary>
        /// Get a specific part by ID.
        /// </summary>
        public static DrawingPart GetPartById(Guid partId)
        {
            var parts = GetParts("WHERE id = @partId", partId);
            return parts.Count > 0 ? parts[0] : null;
        }

        /// <summary>
        /// Get all parts that reference a parent part.
        /// </summary>
        public static List<DrawingPart> GetPartReferences(Guid parentPartId)
        {
            return GetParts("WHERE parent_part_id = @parentPartId", null, parentPartId);
        }

        /// <summary>
        /// Get parts with overrides (need sync).
        /// </summary>
        public static List<DrawingPart> GetOverriddenParts()
        {
            if (CurrentProject == null) return new List<DrawingPart>();
            return GetParts("WHERE project_id = @projectId AND is_override = TRUE");
        }

        private static List<DrawingPart> GetParts(string whereClause, Guid? partId = null, Guid? parentPartId = null)
        {
            var parts = new List<DrawingPart>();
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    $@"SELECT id, project_id, part_name, part_description,
                        source_drawing_name, source_object_handle,
                        parent_part_id, is_original, is_override,
                        geometry_data, geometry_type,
                        position_x, position_y, position_z,
                        rotation_x, rotation_y, rotation_z,
                        scale_x, scale_y, scale_z,
                        layer_name, color_index, material_name,
                        bbox_min_x, bbox_min_y, bbox_min_z,
                        bbox_max_x, bbox_max_y, bbox_max_z,
                        created_at, updated_at, version, material_id
                    FROM public.drawing_parts {whereClause}", conn))
                {
                    if (CurrentProject != null)
                        cmd.Parameters.AddWithValue("projectId", CurrentProject.Id);
                    if (partId.HasValue)
                        cmd.Parameters.AddWithValue("partId", partId.Value);
                    if (parentPartId.HasValue)
                        cmd.Parameters.AddWithValue("parentPartId", parentPartId.Value);

                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            parts.Add(new DrawingPart
                            {
                                Id = r.GetGuid(0),
                                ProjectId = r.GetGuid(1),
                                PartName = r.GetString(2),
                                PartDescription = r.IsDBNull(3) ? null : r.GetString(3),
                                SourceDrawingName = r.GetString(4),
                                SourceObjectHandle = r.GetString(5),
                                ParentPartId = r.IsDBNull(6) ? (Guid?)null : r.GetGuid(6),
                                IsOriginal = r.GetBoolean(7),
                                IsOverride = r.GetBoolean(8),
                                GeometryData = r.IsDBNull(9) ? null : r.GetString(9),
                                GeometryType = r.IsDBNull(10) ? "3DSOLID" : r.GetString(10),
                                PositionX = r.GetDouble(11),
                                PositionY = r.GetDouble(12),
                                PositionZ = r.GetDouble(13),
                                RotationX = r.GetDouble(14),
                                RotationY = r.GetDouble(15),
                                RotationZ = r.GetDouble(16),
                                ScaleX = r.GetDouble(17),
                                ScaleY = r.GetDouble(18),
                                ScaleZ = r.GetDouble(19),
                                LayerName = r.IsDBNull(20) ? "0" : r.GetString(20),
                                ColorIndex = r.GetInt32(21),
                                MaterialName = r.IsDBNull(22) ? null : r.GetString(22),
                                BBoxMinX = r.IsDBNull(23) ? (double?)null : r.GetDouble(23),
                                BBoxMinY = r.IsDBNull(24) ? (double?)null : r.GetDouble(24),
                                BBoxMinZ = r.IsDBNull(25) ? (double?)null : r.GetDouble(25),
                                BBoxMaxX = r.IsDBNull(26) ? (double?)null : r.GetDouble(26),
                                BBoxMaxY = r.IsDBNull(27) ? (double?)null : r.GetDouble(27),
                                BBoxMaxZ = r.IsDBNull(28) ? (double?)null : r.GetDouble(28),
                                CreatedAt = r.GetDateTime(29),
                                UpdatedAt = r.GetDateTime(30),
                                Version = r.GetInt32(31),
                                MaterialId = r.IsDBNull(32) ? (Guid?)null : r.GetGuid(32)
                            });
                        }
                    }
                }
            }
            return parts;
        }

        /// <summary>
        /// Mark a part as overridden and update its geometry.
        /// </summary>
        public static void OverridePart(Guid partId, DrawingPart updatedPart)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    @"UPDATE public.drawing_parts SET
                        is_override = TRUE,
                        geometry_data = @geometryData,
                        position_x = @posX, position_y = @posY, position_z = @posZ,
                        rotation_x = @rotX, rotation_y = @rotY, rotation_z = @rotZ,
                        scale_x = @scaleX, scale_y = @scaleY, scale_z = @scaleZ,
                        layer_name = @layerName, color_index = @colorIndex,
                        bbox_min_x = @bboxMinX, bbox_min_y = @bboxMinY, bbox_min_z = @bboxMinZ,
                        bbox_max_x = @bboxMaxX, bbox_max_y = @bboxMaxY, bbox_max_z = @bboxMaxZ
                    WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", partId);
                    cmd.Parameters.AddWithValue("geometryData", (object)updatedPart.GeometryData ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("posX", updatedPart.PositionX);
                    cmd.Parameters.AddWithValue("posY", updatedPart.PositionY);
                    cmd.Parameters.AddWithValue("posZ", updatedPart.PositionZ);
                    cmd.Parameters.AddWithValue("rotX", updatedPart.RotationX);
                    cmd.Parameters.AddWithValue("rotY", updatedPart.RotationY);
                    cmd.Parameters.AddWithValue("rotZ", updatedPart.RotationZ);
                    cmd.Parameters.AddWithValue("scaleX", updatedPart.ScaleX);
                    cmd.Parameters.AddWithValue("scaleY", updatedPart.ScaleY);
                    cmd.Parameters.AddWithValue("scaleZ", updatedPart.ScaleZ);
                    cmd.Parameters.AddWithValue("layerName", updatedPart.LayerName ?? "0");
                    cmd.Parameters.AddWithValue("colorIndex", updatedPart.ColorIndex);
                    cmd.Parameters.AddWithValue("bboxMinX", (object)updatedPart.BBoxMinX ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("bboxMinY", (object)updatedPart.BBoxMinY ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("bboxMinZ", (object)updatedPart.BBoxMinZ ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("bboxMaxX", (object)updatedPart.BBoxMaxX ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("bboxMaxY", (object)updatedPart.BBoxMaxY ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("bboxMaxZ", (object)updatedPart.BBoxMaxZ ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Apply override to the original part and clear override flag.
        /// </summary>
        public static void ApplyOverrideToOriginal(Guid overridePartId)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                // Get the override part
                DrawingPart overridePart = null;
                using (var cmd = new NpgsqlCommand(
                    "SELECT parent_part_id, geometry_data FROM public.drawing_parts WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", overridePartId);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read() && !r.IsDBNull(0))
                        {
                            overridePart = new DrawingPart
                            {
                                ParentPartId = r.GetGuid(0),
                                GeometryData = r.IsDBNull(1) ? null : r.GetString(1)
                            };
                        }
                    }
                }

                if (overridePart?.ParentPartId == null) return;

                // Update original with override geometry
                using (var cmd = new NpgsqlCommand(
                    @"UPDATE public.drawing_parts SET geometry_data = @geometryData
                      WHERE id = @parentId", conn))
                {
                    cmd.Parameters.AddWithValue("parentId", overridePart.ParentPartId.Value);
                    cmd.Parameters.AddWithValue("geometryData", (object)overridePart.GeometryData ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                // Clear override flag
                using (var cmd = new NpgsqlCommand(
                    "UPDATE public.drawing_parts SET is_override = FALSE WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", overridePartId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Save an inserted part reference.
        /// </summary>
        public static Guid SavePartReference(DrawingPart part, Guid parentPartId)
        {
            part.ParentPartId = parentPartId;
            part.IsOriginal = false;
            part.IsOverride = false;
            return SavePart(part);
        }
    }
}
