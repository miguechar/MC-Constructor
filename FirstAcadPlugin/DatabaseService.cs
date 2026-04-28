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
                using (var cmd = new NpgsqlCommand("SELECT id, name, description FROM public.projects ORDER BY name", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        projects.Add(new Project
                        {
                            Id = reader.GetGuid(0),
                            Name = reader.GetString(1),
                            Description = reader.IsDBNull(2) ? "" : reader.GetString(2)
                        });
                    }
                }
            }
            return projects;
        }

        public static void SetCurrentProject(Project project) => CurrentProject = project;

        /// <summary>
        /// Save a complete part with all geometry data.
        /// </summary>
        public static Guid SavePart(DrawingPart part)
        {
            if (CurrentProject == null)
                throw new InvalidOperationException("No project open. Use MC_OPEN_PROJECT first.");

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
                        bbox_max_x, bbox_max_y, bbox_max_z
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
                        @bboxMaxX, @bboxMaxY, @bboxMaxZ
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
                    is_override = @isOverride
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
                        created_at, updated_at, version
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
                                Version = r.GetInt32(31)
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
