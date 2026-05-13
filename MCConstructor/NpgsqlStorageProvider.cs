using Npgsql;
using System;
using System.Collections.Generic;

namespace MCConstructor
{
    internal class NpgsqlStorageProvider : IStorageProvider
    {
        private string _connectionString;

        public NpgsqlStorageProvider(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string ConnectionString => _connectionString;

        // ====================================================================
        // CONNECTION
        // ====================================================================

        public bool TestConnection(out string errorMessage)
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
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        // ====================================================================
        // PROJECTS
        // ====================================================================

        public List<Project> GetProjects()
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

        public Project CreateProject(string name, string description, string directory)
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
                    return new Project { Id = id, Name = name, Description = description, Directory = directory };
                }
            }
        }

        public bool ProjectNameExists(string name)
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

        public Drawing CreateDrawing(Guid id, string name, string drawingType, string discipline, string filePath, string description)
        {
            if (DatabaseService.CurrentProject == null)
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
                    cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
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
                            ProjectId = DatabaseService.CurrentProject.Id,
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

        public bool DrawingNameExistsInCurrentProject(string name)
        {
            if (DatabaseService.CurrentProject == null) return false;
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    "SELECT 1 FROM public.drawings WHERE project_id = @projectId AND name = @name LIMIT 1", conn))
                {
                    cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
                    cmd.Parameters.AddWithValue("name", name);
                    return cmd.ExecuteScalar() != null;
                }
            }
        }

        public List<Drawing> GetDrawings()
        {
            var drawings = new List<Drawing>();
            if (DatabaseService.CurrentProject == null) return drawings;

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
                    cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            drawings.Add(ReadDrawing(r));
                        }
                    }
                }
            }
            return drawings;
        }

        public Drawing FindNestTemplate()
        {
            if (DatabaseService.CurrentProject == null) return null;

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
                    cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
                    cmd.Parameters.AddWithValue("name", DatabaseService.NestTemplateName.ToUpperInvariant());

                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return null;
                        return ReadDrawing(r);
                    }
                }
            }
        }

        public Drawing GetDrawingByPath(string filePath)
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
                        return ReadDrawing(r);
                    }
                }
            }
        }

        public void UpdateDrawingProperties(Guid drawingId, string drawingType, string discipline, string description)
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

        private static Drawing ReadDrawing(NpgsqlDataReader r)
        {
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

        // ====================================================================
        // PARTS
        // ====================================================================

        public Guid SavePart(DrawingPart part)
        {
            if (DatabaseService.CurrentProject == null)
                throw new InvalidOperationException("No project open. Use MCOpenProject first.");

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                using (var checkCmd = new NpgsqlCommand(
                    @"SELECT id FROM public.drawing_parts
                      WHERE project_id = @projectId
                      AND source_drawing_name = @sourceDrawing
                      AND source_object_handle = @sourceHandle", conn))
                {
                    checkCmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
                    checkCmd.Parameters.AddWithValue("sourceDrawing", part.SourceDrawingName);
                    checkCmd.Parameters.AddWithValue("sourceHandle", part.SourceObjectHandle);

                    var existingId = checkCmd.ExecuteScalar();
                    if (existingId != null && existingId != DBNull.Value)
                        return UpdatePart((Guid)existingId, part, conn);
                }

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
            cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
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

        public List<DrawingPart> GetOriginalParts()
        {
            if (DatabaseService.CurrentProject == null) return new List<DrawingPart>();
            return GetParts("WHERE project_id = @projectId AND is_original = TRUE ORDER BY part_name");
        }

        public DrawingPart GetPartById(Guid partId)
        {
            var parts = GetParts("WHERE id = @partId", partId);
            return parts.Count > 0 ? parts[0] : null;
        }

        public List<DrawingPart> GetPartReferences(Guid parentPartId)
        {
            return GetParts("WHERE parent_part_id = @parentPartId", null, parentPartId);
        }

        public List<DrawingPart> GetOverriddenParts()
        {
            if (DatabaseService.CurrentProject == null) return new List<DrawingPart>();
            return GetParts("WHERE project_id = @projectId AND is_override = TRUE");
        }

        private List<DrawingPart> GetParts(string whereClause, Guid? partId = null, Guid? parentPartId = null)
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
                    if (DatabaseService.CurrentProject != null)
                        cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
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

        public void OverridePart(Guid partId, DrawingPart updatedPart)
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

        public void ApplyOverrideToOriginal(Guid overridePartId)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

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

                using (var cmd = new NpgsqlCommand(
                    "UPDATE public.drawing_parts SET geometry_data = @geometryData WHERE id = @parentId", conn))
                {
                    cmd.Parameters.AddWithValue("parentId", overridePart.ParentPartId.Value);
                    cmd.Parameters.AddWithValue("geometryData", (object)overridePart.GeometryData ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new NpgsqlCommand(
                    "UPDATE public.drawing_parts SET is_override = FALSE WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", overridePartId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public Guid SavePartReference(DrawingPart part, Guid parentPartId)
        {
            part.ParentPartId = parentPartId;
            part.IsOriginal = false;
            part.IsOverride = false;
            return SavePart(part);
        }

        // ====================================================================
        // MATERIALS
        // ====================================================================

        public List<Material> GetMaterials()
        {
            var list = new List<Material>();
            if (DatabaseService.CurrentProject == null) return list;

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    @"SELECT id, project_id, name, description, density, created_at, updated_at
                      FROM public.materials
                      WHERE project_id = @projectId
                      ORDER BY name", conn))
                {
                    cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            list.Add(new Material
                            {
                                Id = r.GetGuid(0),
                                ProjectId = r.GetGuid(1),
                                Name = r.GetString(2),
                                Description = r.IsDBNull(3) ? null : r.GetString(3),
                                Density = r.GetDouble(4),
                                CreatedAt = r.GetDateTime(5),
                                UpdatedAt = r.GetDateTime(6)
                            });
                        }
                    }
                }
            }
            return list;
        }

        public Guid SaveMaterial(Material m)
        {
            if (DatabaseService.CurrentProject == null)
                throw new InvalidOperationException("No project open.");
            if (m == null) throw new ArgumentNullException(nameof(m));
            if (string.IsNullOrWhiteSpace(m.Name))
                throw new ArgumentException("Material name is required.");
            if (m.Density <= 0)
                throw new ArgumentException("Material density must be > 0.");

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                if (m.Id == Guid.Empty)
                {
                    using (var cmd = new NpgsqlCommand(
                        @"INSERT INTO public.materials (project_id, name, description, density)
                          VALUES (@projectId, @name, @description, @density)
                          RETURNING id", conn))
                    {
                        cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
                        cmd.Parameters.AddWithValue("name", m.Name);
                        cmd.Parameters.AddWithValue("description", (object)m.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("density", m.Density);
                        m.Id = (Guid)cmd.ExecuteScalar();
                    }
                }
                else
                {
                    using (var cmd = new NpgsqlCommand(
                        @"UPDATE public.materials
                          SET name = @name, description = @description, density = @density
                          WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("id", m.Id);
                        cmd.Parameters.AddWithValue("name", m.Name);
                        cmd.Parameters.AddWithValue("description", (object)m.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("density", m.Density);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            return m.Id;
        }

        public void DeleteMaterial(Guid id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand("DELETE FROM public.materials WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ====================================================================
        // MATERIAL PLATES
        // ====================================================================

        public List<MaterialPlate> GetPlates(Guid materialId)
        {
            var list = new List<MaterialPlate>();
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    @"SELECT id, material_id, code, width, height, thickness,
                             description, is_default, created_at, updated_at
                      FROM public.material_plates
                      WHERE material_id = @materialId
                      ORDER BY code", conn))
                {
                    cmd.Parameters.AddWithValue("materialId", materialId);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            list.Add(ReadPlate(r));
                    }
                }
            }
            return list;
        }

        public List<MaterialPlate> GetPlatesForCurrentProject()
        {
            var list = new List<MaterialPlate>();
            if (DatabaseService.CurrentProject == null) return list;

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    @"SELECT p.id, p.material_id, p.code, p.width, p.height, p.thickness,
                             p.description, p.is_default, p.created_at, p.updated_at,
                             m.name, m.density
                      FROM public.material_plates p
                      JOIN public.materials m ON m.id = p.material_id
                      WHERE m.project_id = @projectId
                      ORDER BY m.name, p.code", conn))
                {
                    cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var plate = ReadPlate(r);
                            plate.MaterialName = r.IsDBNull(10) ? null : r.GetString(10);
                            plate.MaterialDensity = r.IsDBNull(11) ? 0 : r.GetDouble(11);
                            list.Add(plate);
                        }
                    }
                }
            }
            return list;
        }

        private static MaterialPlate ReadPlate(NpgsqlDataReader r)
        {
            return new MaterialPlate
            {
                Id = r.GetGuid(0),
                MaterialId = r.GetGuid(1),
                Code = r.GetString(2),
                Width = r.GetDouble(3),
                Height = r.GetDouble(4),
                Thickness = r.GetDouble(5),
                Description = r.IsDBNull(6) ? null : r.GetString(6),
                IsDefault = r.GetBoolean(7),
                CreatedAt = r.GetDateTime(8),
                UpdatedAt = r.GetDateTime(9)
            };
        }

        public Guid SavePlate(MaterialPlate p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (p.MaterialId == Guid.Empty)
                throw new ArgumentException("Plate must reference a material.");
            if (string.IsNullOrWhiteSpace(p.Code))
                throw new ArgumentException("Plate code is required (e.g. T04).");
            if (p.Width <= 0 || p.Height <= 0 || p.Thickness <= 0)
                throw new ArgumentException("Plate dimensions must be > 0.");

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                if (p.IsDefault)
                {
                    using (var clear = new NpgsqlCommand(
                        @"UPDATE public.material_plates
                          SET is_default = FALSE
                          WHERE material_id = @materialId
                            AND id <> COALESCE(@selfId, '00000000-0000-0000-0000-000000000000'::uuid)", conn))
                    {
                        clear.Parameters.AddWithValue("materialId", p.MaterialId);
                        clear.Parameters.AddWithValue("selfId",
                            p.Id == Guid.Empty ? (object)DBNull.Value : p.Id);
                        clear.ExecuteNonQuery();
                    }
                }

                if (p.Id == Guid.Empty)
                {
                    using (var cmd = new NpgsqlCommand(
                        @"INSERT INTO public.material_plates
                            (material_id, code, width, height, thickness, description, is_default)
                          VALUES
                            (@materialId, @code, @width, @height, @thickness, @description, @isDefault)
                          RETURNING id", conn))
                    {
                        AddPlateParams(cmd, p);
                        p.Id = (Guid)cmd.ExecuteScalar();
                    }
                }
                else
                {
                    using (var cmd = new NpgsqlCommand(
                        @"UPDATE public.material_plates
                          SET code = @code, width = @width, height = @height,
                              thickness = @thickness, description = @description,
                              is_default = @isDefault
                          WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("id", p.Id);
                        AddPlateParams(cmd, p);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            return p.Id;
        }

        private static void AddPlateParams(NpgsqlCommand cmd, MaterialPlate p)
        {
            cmd.Parameters.AddWithValue("materialId", p.MaterialId);
            cmd.Parameters.AddWithValue("code", p.Code);
            cmd.Parameters.AddWithValue("width", p.Width);
            cmd.Parameters.AddWithValue("height", p.Height);
            cmd.Parameters.AddWithValue("thickness", p.Thickness);
            cmd.Parameters.AddWithValue("description", (object)p.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("isDefault", p.IsDefault);
        }

        public void DeletePlate(Guid id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand("DELETE FROM public.material_plates WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ====================================================================
        // PROFILES
        // ====================================================================

        public List<Profile> GetProfiles()
        {
            var list = new List<Profile>();
            if (DatabaseService.CurrentProject == null) return list;

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    @"SELECT pr.id, pr.project_id, pr.name, pr.code, pr.description,
                             pr.material_id, pr.drawing_id,
                             pr.density_override, pr.cross_section_area,
                             pr.created_at, pr.updated_at,
                             m.name, m.density,
                             d.file_path, d.name
                      FROM public.profiles pr
                      LEFT JOIN public.materials m ON m.id = pr.material_id
                      LEFT JOIN public.drawings  d ON d.id = pr.drawing_id
                      WHERE pr.project_id = @projectId
                      ORDER BY pr.name", conn))
                {
                    cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            list.Add(new Profile
                            {
                                Id = r.GetGuid(0),
                                ProjectId = r.GetGuid(1),
                                Name = r.GetString(2),
                                Code = r.IsDBNull(3) ? null : r.GetString(3),
                                Description = r.IsDBNull(4) ? null : r.GetString(4),
                                MaterialId = r.IsDBNull(5) ? (Guid?)null : r.GetGuid(5),
                                DrawingId = r.IsDBNull(6) ? (Guid?)null : r.GetGuid(6),
                                DensityOverride = r.IsDBNull(7) ? (double?)null : r.GetDouble(7),
                                CrossSectionArea = r.IsDBNull(8) ? (double?)null : r.GetDouble(8),
                                CreatedAt = r.GetDateTime(9),
                                UpdatedAt = r.GetDateTime(10),
                                MaterialName = r.IsDBNull(11) ? null : r.GetString(11),
                                MaterialDensity = r.IsDBNull(12) ? (double?)null : r.GetDouble(12),
                                DrawingFilePath = r.IsDBNull(13) ? null : r.GetString(13),
                                DrawingName = r.IsDBNull(14) ? null : r.GetString(14)
                            });
                        }
                    }
                }
            }
            return list;
        }

        public Guid SaveProfile(Profile p)
        {
            if (DatabaseService.CurrentProject == null)
                throw new InvalidOperationException("No project open.");
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (string.IsNullOrWhiteSpace(p.Name))
                throw new ArgumentException("Profile name is required.");
            if (p.DensityOverride.HasValue && p.DensityOverride.Value <= 0)
                throw new ArgumentException("Density override must be > 0.");

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                if (p.Id == Guid.Empty)
                {
                    using (var cmd = new NpgsqlCommand(
                        @"INSERT INTO public.profiles
                            (project_id, name, code, description, material_id, drawing_id,
                             density_override, cross_section_area)
                          VALUES
                            (@projectId, @name, @code, @description, @materialId, @drawingId,
                             @densityOverride, @area)
                          RETURNING id", conn))
                    {
                        cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
                        AddProfileParams(cmd, p);
                        p.Id = (Guid)cmd.ExecuteScalar();
                    }
                }
                else
                {
                    using (var cmd = new NpgsqlCommand(
                        @"UPDATE public.profiles
                          SET name = @name, code = @code, description = @description,
                              material_id = @materialId, drawing_id = @drawingId,
                              density_override = @densityOverride,
                              cross_section_area = @area
                          WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("id", p.Id);
                        AddProfileParams(cmd, p);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            return p.Id;
        }

        private static void AddProfileParams(NpgsqlCommand cmd, Profile p)
        {
            cmd.Parameters.AddWithValue("name", p.Name);
            cmd.Parameters.AddWithValue("code", (object)p.Code ?? DBNull.Value);
            cmd.Parameters.AddWithValue("description", (object)p.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("materialId", (object)p.MaterialId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("drawingId", (object)p.DrawingId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("densityOverride", (object)p.DensityOverride ?? DBNull.Value);
            cmd.Parameters.AddWithValue("area", (object)p.CrossSectionArea ?? DBNull.Value);
        }

        public void DeleteProfile(Guid id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand("DELETE FROM public.profiles WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<Drawing> GetProfileDrawings()
        {
            var list = new List<Drawing>();
            if (DatabaseService.CurrentProject == null) return list;

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    @"SELECT id, project_id, name, description, drawing_type, discipline,
                             file_path, created_at, updated_at
                      FROM public.drawings
                      WHERE project_id = @projectId AND drawing_type = 'Profile'
                      ORDER BY name", conn))
                {
                    cmd.Parameters.AddWithValue("projectId", DatabaseService.CurrentProject.Id);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            list.Add(ReadDrawing(r));
                    }
                }
            }
            return list;
        }
    }
}
