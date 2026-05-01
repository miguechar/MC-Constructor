using Npgsql;
using System;
using System.Collections.Generic;

namespace FirstAcadPlugin
{
    /// <summary>
    /// CRUD for the material library: <see cref="Material"/>,
    /// <see cref="MaterialPlate"/>, and <see cref="Profile"/>.
    ///
    /// Kept in a separate file so the original <see cref="DatabaseService"/>
    /// stays focused on projects/drawings/parts. All methods reuse
    /// DatabaseService's connection string via
    /// <see cref="DatabaseService.GetConnectionString"/> and require an open
    /// project (<see cref="DatabaseService.CurrentProject"/>) for the
    /// project-scoped lookups.
    /// </summary>
    public static class MaterialLibraryService
    {
        // ====================================================================
        // MATERIALS
        // ====================================================================

        /// <summary>List materials for the open project. Empty list when no project.</summary>
        public static List<Material> GetMaterials()
        {
            var list = new List<Material>();
            if (DatabaseService.CurrentProject == null) return list;

            using (var conn = new NpgsqlConnection(DatabaseService.GetConnectionString()))
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

        /// <summary>
        /// Insert or update. New rows: pass <see cref="Guid.Empty"/> as Id.
        /// Existing rows: pass the row's Id; name/description/density are
        /// updated. Returns the row's Id.
        /// </summary>
        public static Guid SaveMaterial(Material m)
        {
            if (DatabaseService.CurrentProject == null)
                throw new InvalidOperationException("No project open.");
            if (m == null) throw new ArgumentNullException(nameof(m));
            if (string.IsNullOrWhiteSpace(m.Name))
                throw new ArgumentException("Material name is required.");
            if (m.Density <= 0)
                throw new ArgumentException("Material density must be > 0.");

            using (var conn = new NpgsqlConnection(DatabaseService.GetConnectionString()))
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

        /// <summary>Delete by id. Cascade removes plates and clears profile FK.</summary>
        public static void DeleteMaterial(Guid id)
        {
            using (var conn = new NpgsqlConnection(DatabaseService.GetConnectionString()))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    "DELETE FROM public.materials WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ====================================================================
        // MATERIAL PLATES
        // ====================================================================

        /// <summary>All plates for one material, ordered by code.</summary>
        public static List<MaterialPlate> GetPlates(Guid materialId)
        {
            var list = new List<MaterialPlate>();
            using (var conn = new NpgsqlConnection(DatabaseService.GetConnectionString()))
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
                        {
                            list.Add(ReadPlate(r));
                        }
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// Every plate in the open project, joined with its material's name
        /// and density. Used to build the nesting picker. Empty when no
        /// project is open.
        /// </summary>
        public static List<MaterialPlate> GetPlatesForCurrentProject()
        {
            var list = new List<MaterialPlate>();
            if (DatabaseService.CurrentProject == null) return list;

            using (var conn = new NpgsqlConnection(DatabaseService.GetConnectionString()))
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
                            plate.MaterialName    = r.IsDBNull(10) ? null : r.GetString(10);
                            plate.MaterialDensity = r.IsDBNull(11) ? 0    : r.GetDouble(11);
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
                Id          = r.GetGuid(0),
                MaterialId  = r.GetGuid(1),
                Code        = r.GetString(2),
                Width       = r.GetDouble(3),
                Height      = r.GetDouble(4),
                Thickness   = r.GetDouble(5),
                Description = r.IsDBNull(6) ? null : r.GetString(6),
                IsDefault   = r.GetBoolean(7),
                CreatedAt   = r.GetDateTime(8),
                UpdatedAt   = r.GetDateTime(9)
            };
        }

        /// <summary>
        /// Insert or update a plate. When <paramref name="p"/>.IsDefault is
        /// true, automatically clears the flag on the material's other
        /// plates so only one stays marked default.
        /// </summary>
        public static Guid SavePlate(MaterialPlate p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (p.MaterialId == Guid.Empty)
                throw new ArgumentException("Plate must reference a material.");
            if (string.IsNullOrWhiteSpace(p.Code))
                throw new ArgumentException("Plate code is required (e.g. T04).");
            if (p.Width <= 0 || p.Height <= 0 || p.Thickness <= 0)
                throw new ArgumentException("Plate dimensions must be > 0.");

            using (var conn = new NpgsqlConnection(DatabaseService.GetConnectionString()))
            {
                conn.Open();

                // Unset any other defaults under the same material first if
                // we're about to flag this one. Done as a separate UPDATE so
                // the unique-default invariant is enforced even if the
                // INSERT/UPDATE below uses ON CONFLICT semantics later.
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

        public static void DeletePlate(Guid id)
        {
            using (var conn = new NpgsqlConnection(DatabaseService.GetConnectionString()))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    "DELETE FROM public.material_plates WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ====================================================================
        // PROFILES
        // ====================================================================

        /// <summary>
        /// All profiles for the open project, joined with material name +
        /// density and the cross-section drawing's path/name. Empty when no
        /// project is open.
        /// </summary>
        public static List<Profile> GetProfiles()
        {
            var list = new List<Profile>();
            if (DatabaseService.CurrentProject == null) return list;

            using (var conn = new NpgsqlConnection(DatabaseService.GetConnectionString()))
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
                                Id                = r.GetGuid(0),
                                ProjectId         = r.GetGuid(1),
                                Name              = r.GetString(2),
                                Code              = r.IsDBNull(3) ? null : r.GetString(3),
                                Description       = r.IsDBNull(4) ? null : r.GetString(4),
                                MaterialId        = r.IsDBNull(5) ? (Guid?)null : r.GetGuid(5),
                                DrawingId         = r.IsDBNull(6) ? (Guid?)null : r.GetGuid(6),
                                DensityOverride   = r.IsDBNull(7) ? (double?)null : r.GetDouble(7),
                                CrossSectionArea  = r.IsDBNull(8) ? (double?)null : r.GetDouble(8),
                                CreatedAt         = r.GetDateTime(9),
                                UpdatedAt         = r.GetDateTime(10),
                                MaterialName      = r.IsDBNull(11) ? null : r.GetString(11),
                                MaterialDensity   = r.IsDBNull(12) ? (double?)null : r.GetDouble(12),
                                DrawingFilePath   = r.IsDBNull(13) ? null : r.GetString(13),
                                DrawingName       = r.IsDBNull(14) ? null : r.GetString(14)
                            });
                        }
                    }
                }
            }
            return list;
        }

        public static Guid SaveProfile(Profile p)
        {
            if (DatabaseService.CurrentProject == null)
                throw new InvalidOperationException("No project open.");
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (string.IsNullOrWhiteSpace(p.Name))
                throw new ArgumentException("Profile name is required.");
            if (p.DensityOverride.HasValue && p.DensityOverride.Value <= 0)
                throw new ArgumentException("Density override must be > 0.");

            using (var conn = new NpgsqlConnection(DatabaseService.GetConnectionString()))
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

        public static void DeleteProfile(Guid id)
        {
            using (var conn = new NpgsqlConnection(DatabaseService.GetConnectionString()))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(
                    "DELETE FROM public.profiles WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Look up the drawings registered as drawing_type='Profile' for the
        /// open project so the profile editor can show a picker. Returns
        /// empty list when no project.
        /// </summary>
        public static List<Drawing> GetProfileDrawings()
        {
            var list = new List<Drawing>();
            if (DatabaseService.CurrentProject == null) return list;

            using (var conn = new NpgsqlConnection(DatabaseService.GetConnectionString()))
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
                        {
                            list.Add(new Drawing
                            {
                                Id          = r.GetGuid(0),
                                ProjectId   = r.GetGuid(1),
                                Name        = r.GetString(2),
                                Description = r.IsDBNull(3) ? null : r.GetString(3),
                                DrawingType = r.GetString(4),
                                Discipline  = r.IsDBNull(5) ? null : r.GetString(5),
                                FilePath    = r.GetString(6),
                                CreatedAt   = r.GetDateTime(7),
                                UpdatedAt   = r.GetDateTime(8)
                            });
                        }
                    }
                }
            }
            return list;
        }
    }
}
