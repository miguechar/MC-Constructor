using Npgsql;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Exports a postgres project to pro.json and imports pro.json data into postgres.
    /// </summary>
    internal static class JsonMigrationService
    {
        private static readonly JsonSerializerOptions _opts = new JsonSerializerOptions { WriteIndented = true };

        // ====================================================================
        // EXPORT: postgres → pro.json
        // ====================================================================

        /// <summary>
        /// Reads all data for the current project from the active Npgsql provider
        /// and writes a complete pro.json to <paramref name="outputPath"/>.
        /// </summary>
        public static void ExportCurrentProjectToJson(string outputPath)
        {
            if (DatabaseService.CurrentProject == null)
                throw new InvalidOperationException("No project open. Use MCOpenProject first.");

            var project = DatabaseService.CurrentProject;
            string cs = DatabaseService.GetConnectionString();
            if (string.IsNullOrEmpty(cs))
                throw new InvalidOperationException("No database connection string set.");

            // Use a temporary Npgsql provider so we always read from postgres regardless
            // of what the current active provider is.
            var pg = StorageRouter.BuildNpgsqlProvider(cs);

            var drawings = pg.GetDrawings();
            var parts = pg.GetOriginalParts();
            // Also fetch overridden and referenced parts so the export is complete.
            var allParts = new List<DrawingPart>(parts);
            allParts.AddRange(pg.GetOverriddenParts());
            foreach (var op in parts)
                allParts.AddRange(pg.GetPartReferences(op.Id));

            var materials = pg.GetMaterials();
            var plates = pg.GetPlatesForCurrentProject();
            var profiles = pg.GetProfiles();

            var pf = new ProjectFile
            {
                SchemaVersion = 1,
                StorageMode = "json",
                Project = new ProjectFileProject
                {
                    Id = project.Id.ToString(),
                    Name = project.Name,
                    Description = project.Description,
                    Directory = project.Directory,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                Database = !string.IsNullOrEmpty(cs) ? ParseConnectionString(cs) : null,
                Data = new ProjectFileData()
            };

            foreach (var d in drawings)
                pf.Data.Drawings.Add(MapDrawing(d));

            // Deduplicate parts by Id
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var p in allParts)
            {
                string sid = p.Id.ToString();
                if (seen.Add(sid))
                    pf.Data.Parts.Add(MapPart(p));
            }

            foreach (var m in materials)
                pf.Data.Materials.Add(MapMaterial(m));

            foreach (var p in plates)
                pf.Data.MaterialPlates.Add(MapPlate(p));

            foreach (var pr in profiles)
                pf.Data.Profiles.Add(MapProfile(pr));

            string tmp = outputPath + ".tmp";
            string json = JsonSerializer.Serialize(pf, _opts);
            File.WriteAllText(tmp, json, Encoding.UTF8);
            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Move(tmp, outputPath);
        }

        // ====================================================================
        // IMPORT: pro.json → postgres
        // ====================================================================

        /// <summary>
        /// Reads a pro.json and upserts all its data into the postgres database.
        /// Uses INSERT ... ON CONFLICT (id) DO UPDATE for idempotency.
        /// </summary>
        public static void ImportFromJson(string jsonFilePath, string connectionString)
        {
            var pf = ProjectFileWriter.TryLoad(jsonFilePath);
            if (pf?.Project == null)
                throw new InvalidOperationException("Could not read project file.");
            if (pf.Data == null)
                throw new InvalidOperationException("Project file has no data section.");

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();

                // Upsert project
                UpsertProject(conn, pf.Project);

                // Upsert materials first (parts and profiles reference them)
                foreach (var m in pf.Data.Materials)
                    UpsertMaterial(conn, m);

                // Upsert drawings (profiles reference them)
                foreach (var d in pf.Data.Drawings)
                    UpsertDrawing(conn, d);

                // Upsert material plates
                foreach (var p in pf.Data.MaterialPlates)
                    UpsertPlate(conn, p);

                // Upsert profiles
                foreach (var pr in pf.Data.Profiles)
                    UpsertProfile(conn, pr);

                // Upsert parts (parent_part_id self-reference: originals first)
                var originals = pf.Data.Parts.FindAll(p => p.IsOriginal);
                var refs = pf.Data.Parts.FindAll(p => !p.IsOriginal);
                foreach (var p in originals) UpsertPart(conn, p);
                foreach (var p in refs) UpsertPart(conn, p);
            }
        }

        // ====================================================================
        // UPSERT HELPERS
        // ====================================================================

        private static void UpsertProject(NpgsqlConnection conn, ProjectFileProject p)
        {
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO public.projects (id, name, description, directory)
                  VALUES (@id, @name, @description, @directory)
                  ON CONFLICT (id) DO UPDATE
                  SET name = EXCLUDED.name,
                      description = EXCLUDED.description,
                      directory = EXCLUDED.directory,
                      updated_at = NOW()", conn))
            {
                cmd.Parameters.AddWithValue("id", Guid.Parse(p.Id));
                cmd.Parameters.AddWithValue("name", p.Name);
                cmd.Parameters.AddWithValue("description", (object)p.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("directory", p.Directory ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        private static void UpsertDrawing(NpgsqlConnection conn, DrawingRecord d)
        {
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO public.drawings (id, project_id, name, description, drawing_type, discipline, file_path)
                  VALUES (@id, @projectId, @name, @description, @drawingType, @discipline, @filePath)
                  ON CONFLICT (id) DO UPDATE
                  SET name = EXCLUDED.name,
                      description = EXCLUDED.description,
                      drawing_type = EXCLUDED.drawing_type,
                      discipline = EXCLUDED.discipline,
                      file_path = EXCLUDED.file_path,
                      updated_at = NOW()", conn))
            {
                cmd.Parameters.AddWithValue("id", Guid.Parse(d.Id));
                cmd.Parameters.AddWithValue("projectId", Guid.Parse(d.ProjectId));
                cmd.Parameters.AddWithValue("name", d.Name);
                cmd.Parameters.AddWithValue("description", (object)d.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("drawingType", d.DrawingType);
                cmd.Parameters.AddWithValue("discipline", (object)d.Discipline ?? DBNull.Value);
                cmd.Parameters.AddWithValue("filePath", d.FilePath ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        private static void UpsertMaterial(NpgsqlConnection conn, MaterialRecord m)
        {
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO public.materials (id, project_id, name, description, density)
                  VALUES (@id, @projectId, @name, @description, @density)
                  ON CONFLICT (id) DO UPDATE
                  SET name = EXCLUDED.name,
                      description = EXCLUDED.description,
                      density = EXCLUDED.density,
                      updated_at = NOW()", conn))
            {
                cmd.Parameters.AddWithValue("id", Guid.Parse(m.Id));
                cmd.Parameters.AddWithValue("projectId", Guid.Parse(m.ProjectId));
                cmd.Parameters.AddWithValue("name", m.Name);
                cmd.Parameters.AddWithValue("description", (object)m.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("density", m.Density);
                cmd.ExecuteNonQuery();
            }
        }

        private static void UpsertPlate(NpgsqlConnection conn, MaterialPlateRecord p)
        {
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO public.material_plates (id, material_id, code, width, height, thickness, description, is_default)
                  VALUES (@id, @materialId, @code, @width, @height, @thickness, @description, @isDefault)
                  ON CONFLICT (id) DO UPDATE
                  SET code = EXCLUDED.code,
                      width = EXCLUDED.width,
                      height = EXCLUDED.height,
                      thickness = EXCLUDED.thickness,
                      description = EXCLUDED.description,
                      is_default = EXCLUDED.is_default,
                      updated_at = NOW()", conn))
            {
                cmd.Parameters.AddWithValue("id", Guid.Parse(p.Id));
                cmd.Parameters.AddWithValue("materialId", Guid.Parse(p.MaterialId));
                cmd.Parameters.AddWithValue("code", p.Code);
                cmd.Parameters.AddWithValue("width", p.Width);
                cmd.Parameters.AddWithValue("height", p.Height);
                cmd.Parameters.AddWithValue("thickness", p.Thickness);
                cmd.Parameters.AddWithValue("description", (object)p.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("isDefault", p.IsDefault);
                cmd.ExecuteNonQuery();
            }
        }

        private static void UpsertProfile(NpgsqlConnection conn, ProfileRecord pr)
        {
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO public.profiles (id, project_id, name, code, description, material_id, drawing_id, density_override, cross_section_area)
                  VALUES (@id, @projectId, @name, @code, @description, @materialId, @drawingId, @densityOverride, @area)
                  ON CONFLICT (id) DO UPDATE
                  SET name = EXCLUDED.name,
                      code = EXCLUDED.code,
                      description = EXCLUDED.description,
                      material_id = EXCLUDED.material_id,
                      drawing_id = EXCLUDED.drawing_id,
                      density_override = EXCLUDED.density_override,
                      cross_section_area = EXCLUDED.cross_section_area,
                      updated_at = NOW()", conn))
            {
                cmd.Parameters.AddWithValue("id", Guid.Parse(pr.Id));
                cmd.Parameters.AddWithValue("projectId", Guid.Parse(pr.ProjectId));
                cmd.Parameters.AddWithValue("name", pr.Name);
                cmd.Parameters.AddWithValue("code", (object)pr.Code ?? DBNull.Value);
                cmd.Parameters.AddWithValue("description", (object)pr.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("materialId", string.IsNullOrEmpty(pr.MaterialId) ? (object)DBNull.Value : Guid.Parse(pr.MaterialId));
                cmd.Parameters.AddWithValue("drawingId", string.IsNullOrEmpty(pr.DrawingId) ? (object)DBNull.Value : Guid.Parse(pr.DrawingId));
                cmd.Parameters.AddWithValue("densityOverride", (object)pr.DensityOverride ?? DBNull.Value);
                cmd.Parameters.AddWithValue("area", (object)pr.CrossSectionArea ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        private static void UpsertPart(NpgsqlConnection conn, DrawingPartRecord p)
        {
            using (var cmd = new NpgsqlCommand(
                @"INSERT INTO public.drawing_parts (
                    id, project_id, part_name, part_description,
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
                    @id, @projectId, @partName, @partDesc,
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
                  )
                  ON CONFLICT (id) DO UPDATE
                  SET part_name = EXCLUDED.part_name,
                      part_description = EXCLUDED.part_description,
                      geometry_data = EXCLUDED.geometry_data,
                      geometry_type = EXCLUDED.geometry_type,
                      position_x = EXCLUDED.position_x,
                      position_y = EXCLUDED.position_y,
                      position_z = EXCLUDED.position_z,
                      rotation_x = EXCLUDED.rotation_x,
                      rotation_y = EXCLUDED.rotation_y,
                      rotation_z = EXCLUDED.rotation_z,
                      scale_x = EXCLUDED.scale_x,
                      scale_y = EXCLUDED.scale_y,
                      scale_z = EXCLUDED.scale_z,
                      layer_name = EXCLUDED.layer_name,
                      color_index = EXCLUDED.color_index,
                      material_name = EXCLUDED.material_name,
                      bbox_min_x = EXCLUDED.bbox_min_x,
                      bbox_min_y = EXCLUDED.bbox_min_y,
                      bbox_min_z = EXCLUDED.bbox_min_z,
                      bbox_max_x = EXCLUDED.bbox_max_x,
                      bbox_max_y = EXCLUDED.bbox_max_y,
                      bbox_max_z = EXCLUDED.bbox_max_z,
                      is_override = EXCLUDED.is_override,
                      material_id = EXCLUDED.material_id,
                      updated_at = NOW()", conn))
            {
                cmd.Parameters.AddWithValue("id", Guid.Parse(p.Id));
                cmd.Parameters.AddWithValue("projectId", Guid.Parse(p.ProjectId));
                cmd.Parameters.AddWithValue("partName", p.PartName ?? "");
                cmd.Parameters.AddWithValue("partDesc", (object)p.PartDescription ?? DBNull.Value);
                cmd.Parameters.AddWithValue("sourceDrawing", p.SourceDrawingName ?? "");
                cmd.Parameters.AddWithValue("sourceHandle", p.SourceObjectHandle ?? "");
                cmd.Parameters.AddWithValue("parentPartId", string.IsNullOrEmpty(p.ParentPartId) ? (object)DBNull.Value : Guid.Parse(p.ParentPartId));
                cmd.Parameters.AddWithValue("isOriginal", p.IsOriginal);
                cmd.Parameters.AddWithValue("isOverride", p.IsOverride);
                cmd.Parameters.AddWithValue("geometryData", (object)p.GeometryData ?? DBNull.Value);
                cmd.Parameters.AddWithValue("geometryType", p.GeometryType ?? "3DSOLID");
                cmd.Parameters.AddWithValue("posX", p.PositionX);
                cmd.Parameters.AddWithValue("posY", p.PositionY);
                cmd.Parameters.AddWithValue("posZ", p.PositionZ);
                cmd.Parameters.AddWithValue("rotX", p.RotationX);
                cmd.Parameters.AddWithValue("rotY", p.RotationY);
                cmd.Parameters.AddWithValue("rotZ", p.RotationZ);
                cmd.Parameters.AddWithValue("scaleX", p.ScaleX);
                cmd.Parameters.AddWithValue("scaleY", p.ScaleY);
                cmd.Parameters.AddWithValue("scaleZ", p.ScaleZ);
                cmd.Parameters.AddWithValue("layerName", p.LayerName ?? "0");
                cmd.Parameters.AddWithValue("colorIndex", p.ColorIndex);
                cmd.Parameters.AddWithValue("materialName", (object)p.MaterialName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("bboxMinX", (object)p.BBoxMinX ?? DBNull.Value);
                cmd.Parameters.AddWithValue("bboxMinY", (object)p.BBoxMinY ?? DBNull.Value);
                cmd.Parameters.AddWithValue("bboxMinZ", (object)p.BBoxMinZ ?? DBNull.Value);
                cmd.Parameters.AddWithValue("bboxMaxX", (object)p.BBoxMaxX ?? DBNull.Value);
                cmd.Parameters.AddWithValue("bboxMaxY", (object)p.BBoxMaxY ?? DBNull.Value);
                cmd.Parameters.AddWithValue("bboxMaxZ", (object)p.BBoxMaxZ ?? DBNull.Value);
                cmd.Parameters.AddWithValue("materialId", string.IsNullOrEmpty(p.MaterialId) ? (object)DBNull.Value : Guid.Parse(p.MaterialId));
                cmd.ExecuteNonQuery();
            }
        }

        // ====================================================================
        // MAPPING: domain → record
        // ====================================================================

        private static DrawingRecord MapDrawing(Drawing d)
        {
            return new DrawingRecord
            {
                Id = d.Id.ToString(),
                ProjectId = d.ProjectId.ToString(),
                Name = d.Name,
                Description = d.Description,
                DrawingType = d.DrawingType,
                Discipline = d.Discipline,
                FilePath = d.FilePath,
                CreatedAt = d.CreatedAt.ToString("o"),
                UpdatedAt = d.UpdatedAt.ToString("o")
            };
        }

        private static DrawingPartRecord MapPart(DrawingPart p)
        {
            return new DrawingPartRecord
            {
                Id = p.Id.ToString(),
                ProjectId = p.ProjectId.ToString(),
                PartName = p.PartName,
                PartDescription = p.PartDescription,
                SourceDrawingName = p.SourceDrawingName,
                SourceObjectHandle = p.SourceObjectHandle,
                ParentPartId = p.ParentPartId?.ToString(),
                IsOriginal = p.IsOriginal,
                IsOverride = p.IsOverride,
                GeometryData = p.GeometryData,
                GeometryType = p.GeometryType,
                PositionX = p.PositionX,
                PositionY = p.PositionY,
                PositionZ = p.PositionZ,
                RotationX = p.RotationX,
                RotationY = p.RotationY,
                RotationZ = p.RotationZ,
                ScaleX = p.ScaleX,
                ScaleY = p.ScaleY,
                ScaleZ = p.ScaleZ,
                LayerName = p.LayerName,
                ColorIndex = p.ColorIndex,
                MaterialName = p.MaterialName,
                BBoxMinX = p.BBoxMinX,
                BBoxMinY = p.BBoxMinY,
                BBoxMinZ = p.BBoxMinZ,
                BBoxMaxX = p.BBoxMaxX,
                BBoxMaxY = p.BBoxMaxY,
                BBoxMaxZ = p.BBoxMaxZ,
                MaterialId = p.MaterialId?.ToString(),
                CreatedAt = p.CreatedAt.ToString("o"),
                UpdatedAt = p.UpdatedAt.ToString("o"),
                Version = p.Version
            };
        }

        private static MaterialRecord MapMaterial(Material m)
        {
            return new MaterialRecord
            {
                Id = m.Id.ToString(),
                ProjectId = m.ProjectId.ToString(),
                Name = m.Name,
                Description = m.Description,
                Density = m.Density,
                CreatedAt = m.CreatedAt.ToString("o"),
                UpdatedAt = m.UpdatedAt.ToString("o")
            };
        }

        private static MaterialPlateRecord MapPlate(MaterialPlate p)
        {
            return new MaterialPlateRecord
            {
                Id = p.Id.ToString(),
                MaterialId = p.MaterialId.ToString(),
                Code = p.Code,
                Width = p.Width,
                Height = p.Height,
                Thickness = p.Thickness,
                Description = p.Description,
                IsDefault = p.IsDefault,
                CreatedAt = p.CreatedAt.ToString("o"),
                UpdatedAt = p.UpdatedAt.ToString("o")
            };
        }

        private static ProfileRecord MapProfile(Profile pr)
        {
            return new ProfileRecord
            {
                Id = pr.Id.ToString(),
                ProjectId = pr.ProjectId.ToString(),
                Name = pr.Name,
                Code = pr.Code,
                Description = pr.Description,
                MaterialId = pr.MaterialId?.ToString(),
                DrawingId = pr.DrawingId?.ToString(),
                DensityOverride = pr.DensityOverride,
                CrossSectionArea = pr.CrossSectionArea,
                CreatedAt = pr.CreatedAt.ToString("o"),
                UpdatedAt = pr.UpdatedAt.ToString("o")
            };
        }

        private static ProjectFileDatabaseConfig ParseConnectionString(string cs)
        {
            var cfg = new ProjectFileDatabaseConfig { Port = 5432 };
            foreach (var part in cs.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                string key = part.Substring(0, eq).Trim().ToLowerInvariant();
                string val = part.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "host":     cfg.Host = val;    break;
                    case "port":     int.TryParse(val, out int port); cfg.Port = port; break;
                    case "database": cfg.Database = val; break;
                    case "username": cfg.Username = val; break;
                    case "password": cfg.Password = val; break;
                }
            }
            return cfg;
        }
    }
}
