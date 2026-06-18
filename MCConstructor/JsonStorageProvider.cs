using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MCConstructor
{
    internal class JsonStorageProvider : IStorageProvider
    {
        private string _filePath;
        private static readonly JsonSerializerOptions _opts = new JsonSerializerOptions { WriteIndented = true };

        public JsonStorageProvider(string filePath)
        {
            _filePath = filePath;
        }

        public string FilePath => _filePath;

        // ====================================================================
        // FILE I/O
        // ====================================================================

        private ProjectFile Load()
        {
            // Recover from a crashed mid-write: tmp exists but original was already deleted
            string tmp = _filePath + ".tmp";
            if (!File.Exists(_filePath) && File.Exists(tmp))
                File.Move(tmp, _filePath);

            string json = File.ReadAllText(_filePath, Encoding.UTF8);
            var pf = JsonSerializer.Deserialize<ProjectFile>(json, _opts);
            if (pf.Data == null) pf.Data = new ProjectFileData();
            return pf;
        }

        private void Save(ProjectFile pf)
        {
            string tmp = _filePath + ".tmp";
            string json = JsonSerializer.Serialize(pf, _opts);
            File.WriteAllText(tmp, json, Encoding.UTF8);
            if (File.Exists(_filePath))
                File.Delete(_filePath);
            File.Move(tmp, _filePath);
        }

        // ====================================================================
        // CONNECTION — always succeeds for file-based storage
        // ====================================================================

        public bool TestConnection(out string errorMessage)
        {
            errorMessage = null;
            return true;
        }

        // ====================================================================
        // PROJECTS
        // ====================================================================

        public List<Project> GetProjects()
        {
            var pf = Load();
            return new List<Project> { MapProject(pf.Project) };
        }

        public Project CreateProject(string name, string description, string directory)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Project name is required.", nameof(name));
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Project directory is required.", nameof(directory));

            var pf = Load();
            var now = DateTime.UtcNow.ToString("o");
            var id = Guid.NewGuid();

            pf.Project = new ProjectFileProject
            {
                Id = id.ToString(),
                Name = name,
                Description = description,
                Directory = directory,
                CreatedAt = now,
                UpdatedAt = now
            };
            Save(pf);

            return new Project { Id = id, Name = name, Description = description, Directory = directory };
        }

        public bool ProjectNameExists(string name)
        {
            // Each pro.json is a single project; name collisions are not enforced across files.
            return false;
        }

        private static Project MapProject(ProjectFileProject p)
        {
            return new Project
            {
                Id = Guid.Parse(p.Id),
                Name = p.Name,
                Description = p.Description ?? "",
                Directory = p.Directory ?? ""
            };
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

            var pf = Load();
            var now = DateTime.UtcNow.ToString("o");
            var drawingId = id == Guid.Empty ? Guid.NewGuid() : id;

            pf.Data.Drawings.Add(new DrawingRecord
            {
                Id = drawingId.ToString(),
                ProjectId = DatabaseService.CurrentProject.Id.ToString(),
                Name = name,
                Description = description,
                DrawingType = drawingType,
                Discipline = discipline,
                FilePath = filePath,
                CreatedAt = now,
                UpdatedAt = now
            });
            Save(pf);

            return new Drawing
            {
                Id = drawingId,
                ProjectId = DatabaseService.CurrentProject.Id,
                Name = name,
                Description = description,
                DrawingType = drawingType,
                Discipline = discipline,
                FilePath = filePath,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        public bool DrawingNameExistsInCurrentProject(string name)
        {
            if (DatabaseService.CurrentProject == null) return false;
            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();
            return pf.Data.Drawings.Exists(d =>
                d.ProjectId == projectId &&
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public List<Drawing> GetDrawings()
        {
            var result = new List<Drawing>();
            if (DatabaseService.CurrentProject == null) return result;
            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();
            foreach (var d in pf.Data.Drawings)
            {
                if (d.ProjectId == projectId)
                    result.Add(MapDrawing(d));
            }
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public Drawing FindNestTemplate()
        {
            if (DatabaseService.CurrentProject == null) return null;
            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();
            string templateName = DatabaseService.NestTemplateName.ToUpperInvariant();
            var rec = pf.Data.Drawings.Find(d =>
                d.ProjectId == projectId &&
                d.DrawingType == DrawingTypes.Template &&
                (d.Name ?? "").ToUpperInvariant() == templateName);
            return rec != null ? MapDrawing(rec) : null;
        }

        public Drawing GetDrawingByPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            var pf = Load();
            var rec = pf.Data.Drawings.Find(d =>
                string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            return rec != null ? MapDrawing(rec) : null;
        }

        public void UpdateDrawingProperties(Guid drawingId, string drawingType, string discipline, string description)
        {
            var pf = Load();
            string id = drawingId.ToString();
            var rec = pf.Data.Drawings.Find(d => d.Id == id);
            if (rec == null) return;
            rec.DrawingType = drawingType;
            rec.Discipline = discipline;
            rec.Description = description;
            rec.UpdatedAt = DateTime.UtcNow.ToString("o");
            Save(pf);
        }

        public int DeleteDrawing(Guid drawingId, string drawingName, string filePath)
        {
            if (DatabaseService.CurrentProject == null)
                throw new InvalidOperationException("No project open. Use MCOpenProject first.");

            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();
            string drawingIdText = drawingId.ToString();

            int drawingsDeleted = pf.Data.Drawings.RemoveAll(d =>
                d.ProjectId == projectId && d.Id == drawingIdText);
            if (drawingsDeleted != 1)
                throw new InvalidOperationException("Drawing was not found in the current project.");

            var sourceNames = DrawingSourceNames(drawingName, filePath);
            int partsDeleted = pf.Data.Parts.RemoveAll(p =>
                p.ProjectId == projectId &&
                sourceNames.Exists(n => string.Equals(n, p.SourceDrawingName, StringComparison.OrdinalIgnoreCase)));

            foreach (var profile in pf.Data.Profiles)
            {
                if (profile.ProjectId == projectId && profile.DrawingId == drawingIdText)
                    profile.DrawingId = null;
            }

            foreach (var nest in pf.Data.Nests)
            {
                if (nest.ProjectId == projectId && nest.DrawingId == drawingIdText)
                    nest.DrawingId = null;
            }

            Save(pf);
            return partsDeleted;
        }

        private static List<string> DrawingSourceNames(string drawingName, string filePath)
        {
            var names = new List<string>();
            AddUnique(names, drawingName);

            if (!string.IsNullOrWhiteSpace(drawingName) &&
                string.IsNullOrEmpty(Path.GetExtension(drawingName)))
                AddUnique(names, drawingName + ".dwg");

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                AddUnique(names, Path.GetFileName(filePath));
                AddUnique(names, Path.GetFileNameWithoutExtension(filePath));
            }

            return names;
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (var existing in list)
            {
                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(value);
        }

        private static Drawing MapDrawing(DrawingRecord d)
        {
            return new Drawing
            {
                Id = Guid.Parse(d.Id),
                ProjectId = Guid.Parse(d.ProjectId),
                Name = d.Name,
                Description = d.Description,
                DrawingType = d.DrawingType,
                Discipline = d.Discipline,
                FilePath = d.FilePath,
                CreatedAt = ParseDate(d.CreatedAt),
                UpdatedAt = ParseDate(d.UpdatedAt)
            };
        }

        // ====================================================================
        // PARTS
        // ====================================================================

        public Guid SavePart(DrawingPart part)
        {
            if (DatabaseService.CurrentProject == null)
                throw new InvalidOperationException("No project open. Use MCOpenProject first.");

            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();
            var now = DateTime.UtcNow.ToString("o");

            var existing = pf.Data.Parts.Find(p =>
                p.ProjectId == projectId &&
                p.SourceDrawingName == part.SourceDrawingName &&
                p.SourceObjectHandle == part.SourceObjectHandle);

            if (existing != null)
            {
                MapPartToRecord(existing, part, projectId);
                existing.UpdatedAt = now;
                Save(pf);
                return Guid.Parse(existing.Id);
            }

            var rec = new DrawingPartRecord { Id = Guid.NewGuid().ToString(), CreatedAt = now };
            MapPartToRecord(rec, part, projectId);
            rec.UpdatedAt = now;
            pf.Data.Parts.Add(rec);
            Save(pf);
            return Guid.Parse(rec.Id);
        }

        public List<DrawingPart> GetOriginalParts()
        {
            if (DatabaseService.CurrentProject == null) return new List<DrawingPart>();
            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();
            var result = new List<DrawingPart>();
            foreach (var p in pf.Data.Parts)
            {
                if (p.ProjectId == projectId && p.IsOriginal)
                    result.Add(MapPart(p));
            }
            result.Sort((a, b) => string.Compare(a.PartName, b.PartName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public DrawingPart GetPartById(Guid partId)
        {
            var pf = Load();
            string id = partId.ToString();
            var rec = pf.Data.Parts.Find(p => p.Id == id);
            return rec != null ? MapPart(rec) : null;
        }

        public List<DrawingPart> GetPartReferences(Guid parentPartId)
        {
            var pf = Load();
            string parentId = parentPartId.ToString();
            var result = new List<DrawingPart>();
            foreach (var p in pf.Data.Parts)
            {
                if (p.ParentPartId == parentId)
                    result.Add(MapPart(p));
            }
            return result;
        }

        public List<DrawingPart> GetOverriddenParts()
        {
            if (DatabaseService.CurrentProject == null) return new List<DrawingPart>();
            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();
            var result = new List<DrawingPart>();
            foreach (var p in pf.Data.Parts)
            {
                if (p.ProjectId == projectId && p.IsOverride)
                    result.Add(MapPart(p));
            }
            return result;
        }

        public void OverridePart(Guid partId, DrawingPart updatedPart)
        {
            var pf = Load();
            string id = partId.ToString();
            var rec = pf.Data.Parts.Find(p => p.Id == id);
            if (rec == null) return;

            rec.IsOverride = true;
            rec.GeometryData = updatedPart.GeometryData;
            rec.PositionX = updatedPart.PositionX;
            rec.PositionY = updatedPart.PositionY;
            rec.PositionZ = updatedPart.PositionZ;
            rec.RotationX = updatedPart.RotationX;
            rec.RotationY = updatedPart.RotationY;
            rec.RotationZ = updatedPart.RotationZ;
            rec.ScaleX = updatedPart.ScaleX;
            rec.ScaleY = updatedPart.ScaleY;
            rec.ScaleZ = updatedPart.ScaleZ;
            rec.LayerName = updatedPart.LayerName ?? "0";
            rec.ColorIndex = updatedPart.ColorIndex;
            rec.BBoxMinX = updatedPart.BBoxMinX;
            rec.BBoxMinY = updatedPart.BBoxMinY;
            rec.BBoxMinZ = updatedPart.BBoxMinZ;
            rec.BBoxMaxX = updatedPart.BBoxMaxX;
            rec.BBoxMaxY = updatedPart.BBoxMaxY;
            rec.BBoxMaxZ = updatedPart.BBoxMaxZ;
            rec.UpdatedAt = DateTime.UtcNow.ToString("o");
            Save(pf);
        }

        public void ApplyOverrideToOriginal(Guid overridePartId)
        {
            var pf = Load();
            string id = overridePartId.ToString();
            var overrideRec = pf.Data.Parts.Find(p => p.Id == id);
            if (overrideRec?.ParentPartId == null) return;

            var originalRec = pf.Data.Parts.Find(p => p.Id == overrideRec.ParentPartId);
            if (originalRec == null) return;

            originalRec.GeometryData = overrideRec.GeometryData;
            originalRec.UpdatedAt = DateTime.UtcNow.ToString("o");
            overrideRec.IsOverride = false;
            overrideRec.UpdatedAt = DateTime.UtcNow.ToString("o");
            Save(pf);
        }

        public Guid SavePartReference(DrawingPart part, Guid parentPartId)
        {
            part.ParentPartId = parentPartId;
            part.IsOriginal = false;
            part.IsOverride = false;
            return SavePart(part);
        }

        private static void MapPartToRecord(DrawingPartRecord rec, DrawingPart part, string projectId)
        {
            rec.ProjectId = projectId;
            rec.PartName = part.PartName ?? "";
            rec.PartDescription = part.PartDescription;
            rec.SourceDrawingName = part.SourceDrawingName;
            rec.SourceObjectHandle = part.SourceObjectHandle;
            rec.ParentPartId = part.ParentPartId?.ToString();
            rec.IsOriginal = part.IsOriginal;
            rec.IsOverride = part.IsOverride;
            rec.GeometryData = part.GeometryData;
            rec.GeometryType = part.GeometryType ?? "3DSOLID";
            rec.PositionX = part.PositionX;
            rec.PositionY = part.PositionY;
            rec.PositionZ = part.PositionZ;
            rec.RotationX = part.RotationX;
            rec.RotationY = part.RotationY;
            rec.RotationZ = part.RotationZ;
            rec.ScaleX = part.ScaleX;
            rec.ScaleY = part.ScaleY;
            rec.ScaleZ = part.ScaleZ;
            rec.LayerName = part.LayerName ?? "0";
            rec.ColorIndex = part.ColorIndex;
            rec.MaterialName = part.MaterialName;
            rec.BBoxMinX = part.BBoxMinX;
            rec.BBoxMinY = part.BBoxMinY;
            rec.BBoxMinZ = part.BBoxMinZ;
            rec.BBoxMaxX = part.BBoxMaxX;
            rec.BBoxMaxY = part.BBoxMaxY;
            rec.BBoxMaxZ = part.BBoxMaxZ;
            rec.MaterialId = part.MaterialId?.ToString();
            rec.Version = part.Version;
        }

        private static DrawingPart MapPart(DrawingPartRecord p)
        {
            return new DrawingPart
            {
                Id = Guid.Parse(p.Id),
                ProjectId = Guid.Parse(p.ProjectId),
                PartName = p.PartName,
                PartDescription = p.PartDescription,
                SourceDrawingName = p.SourceDrawingName,
                SourceObjectHandle = p.SourceObjectHandle,
                ParentPartId = string.IsNullOrEmpty(p.ParentPartId) ? (Guid?)null : Guid.Parse(p.ParentPartId),
                IsOriginal = p.IsOriginal,
                IsOverride = p.IsOverride,
                GeometryData = p.GeometryData,
                GeometryType = p.GeometryType ?? "3DSOLID",
                PositionX = p.PositionX,
                PositionY = p.PositionY,
                PositionZ = p.PositionZ,
                RotationX = p.RotationX,
                RotationY = p.RotationY,
                RotationZ = p.RotationZ,
                ScaleX = p.ScaleX,
                ScaleY = p.ScaleY,
                ScaleZ = p.ScaleZ,
                LayerName = p.LayerName ?? "0",
                ColorIndex = p.ColorIndex,
                MaterialName = p.MaterialName,
                BBoxMinX = p.BBoxMinX,
                BBoxMinY = p.BBoxMinY,
                BBoxMinZ = p.BBoxMinZ,
                BBoxMaxX = p.BBoxMaxX,
                BBoxMaxY = p.BBoxMaxY,
                BBoxMaxZ = p.BBoxMaxZ,
                MaterialId = string.IsNullOrEmpty(p.MaterialId) ? (Guid?)null : Guid.Parse(p.MaterialId),
                CreatedAt = ParseDate(p.CreatedAt),
                UpdatedAt = ParseDate(p.UpdatedAt),
                Version = p.Version
            };
        }

        // ====================================================================
        // MATERIALS
        // ====================================================================

        public List<Material> GetMaterials()
        {
            var result = new List<Material>();
            if (DatabaseService.CurrentProject == null) return result;
            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();
            foreach (var m in pf.Data.Materials)
            {
                if (m.ProjectId == projectId)
                    result.Add(MapMaterial(m));
            }
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
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

            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();
            var now = DateTime.UtcNow.ToString("o");

            if (m.Id == Guid.Empty)
            {
                m.Id = Guid.NewGuid();
                pf.Data.Materials.Add(new MaterialRecord
                {
                    Id = m.Id.ToString(),
                    ProjectId = projectId,
                    Name = m.Name,
                    Description = m.Description,
                    Density = m.Density,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                var rec = pf.Data.Materials.Find(r => r.Id == m.Id.ToString());
                if (rec != null)
                {
                    rec.Name = m.Name;
                    rec.Description = m.Description;
                    rec.Density = m.Density;
                    rec.UpdatedAt = now;
                }
            }
            Save(pf);
            return m.Id;
        }

        public void DeleteMaterial(Guid id)
        {
            var pf = Load();
            string sid = id.ToString();
            pf.Data.Materials.RemoveAll(m => m.Id == sid);
            // Cascade: remove plates
            pf.Data.MaterialPlates.RemoveAll(p => p.MaterialId == sid);
            // Cascade: clear FK on profiles
            foreach (var pr in pf.Data.Profiles)
            {
                if (pr.MaterialId == sid) pr.MaterialId = null;
            }
            Save(pf);
        }

        private static Material MapMaterial(MaterialRecord m)
        {
            return new Material
            {
                Id = Guid.Parse(m.Id),
                ProjectId = Guid.Parse(m.ProjectId),
                Name = m.Name,
                Description = m.Description,
                Density = m.Density,
                CreatedAt = ParseDate(m.CreatedAt),
                UpdatedAt = ParseDate(m.UpdatedAt)
            };
        }

        // ====================================================================
        // MATERIAL PLATES
        // ====================================================================

        public List<MaterialPlate> GetPlates(Guid materialId)
        {
            var pf = Load();
            string mid = materialId.ToString();
            var result = new List<MaterialPlate>();
            foreach (var p in pf.Data.MaterialPlates)
            {
                if (p.MaterialId == mid)
                    result.Add(MapPlate(p, null, 0));
            }
            result.Sort((a, b) => string.Compare(a.Code, b.Code, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public List<MaterialPlate> GetPlatesForCurrentProject()
        {
            var result = new List<MaterialPlate>();
            if (DatabaseService.CurrentProject == null) return result;
            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();

            // Build material lookup for join
            var materialLookup = new Dictionary<string, MaterialRecord>();
            foreach (var m in pf.Data.Materials)
            {
                if (m.ProjectId == projectId)
                    materialLookup[m.Id] = m;
            }

            foreach (var p in pf.Data.MaterialPlates)
            {
                if (materialLookup.TryGetValue(p.MaterialId ?? "", out var mat))
                {
                    result.Add(MapPlate(p, mat.Name, mat.Density));
                }
            }
            result.Sort((a, b) =>
            {
                int cmp = string.Compare(a.MaterialName, b.MaterialName, StringComparison.OrdinalIgnoreCase);
                return cmp != 0 ? cmp : string.Compare(a.Code, b.Code, StringComparison.OrdinalIgnoreCase);
            });
            return result;
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

            var pf = Load();
            string mid = p.MaterialId.ToString();
            var now = DateTime.UtcNow.ToString("o");

            // Enforce single-default invariant
            if (p.IsDefault)
            {
                string selfId = p.Id == Guid.Empty ? null : p.Id.ToString();
                foreach (var other in pf.Data.MaterialPlates)
                {
                    if (other.MaterialId == mid && other.Id != selfId)
                        other.IsDefault = false;
                }
            }

            if (p.Id == Guid.Empty)
            {
                p.Id = Guid.NewGuid();
                pf.Data.MaterialPlates.Add(new MaterialPlateRecord
                {
                    Id = p.Id.ToString(),
                    MaterialId = mid,
                    Code = p.Code,
                    Width = p.Width,
                    Height = p.Height,
                    Thickness = p.Thickness,
                    Description = p.Description,
                    IsDefault = p.IsDefault,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                var rec = pf.Data.MaterialPlates.Find(r => r.Id == p.Id.ToString());
                if (rec != null)
                {
                    rec.Code = p.Code;
                    rec.Width = p.Width;
                    rec.Height = p.Height;
                    rec.Thickness = p.Thickness;
                    rec.Description = p.Description;
                    rec.IsDefault = p.IsDefault;
                    rec.UpdatedAt = now;
                }
            }
            Save(pf);
            return p.Id;
        }

        public void DeletePlate(Guid id)
        {
            var pf = Load();
            pf.Data.MaterialPlates.RemoveAll(p => p.Id == id.ToString());
            Save(pf);
        }

        private static MaterialPlate MapPlate(MaterialPlateRecord p, string materialName, double materialDensity)
        {
            return new MaterialPlate
            {
                Id = Guid.Parse(p.Id),
                MaterialId = Guid.Parse(p.MaterialId),
                Code = p.Code,
                Width = p.Width,
                Height = p.Height,
                Thickness = p.Thickness,
                Description = p.Description,
                IsDefault = p.IsDefault,
                CreatedAt = ParseDate(p.CreatedAt),
                UpdatedAt = ParseDate(p.UpdatedAt),
                MaterialName = materialName,
                MaterialDensity = materialDensity
            };
        }

        // ====================================================================
        // PROFILES
        // ====================================================================

        public List<Profile> GetProfiles()
        {
            var result = new List<Profile>();
            if (DatabaseService.CurrentProject == null) return result;
            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();

            // Build lookups for join
            var materialLookup = new Dictionary<string, MaterialRecord>();
            foreach (var m in pf.Data.Materials)
                materialLookup[m.Id] = m;

            var drawingLookup = new Dictionary<string, DrawingRecord>();
            foreach (var d in pf.Data.Drawings)
                drawingLookup[d.Id] = d;

            foreach (var pr in pf.Data.Profiles)
            {
                if (pr.ProjectId != projectId) continue;
                materialLookup.TryGetValue(pr.MaterialId ?? "", out var mat);
                drawingLookup.TryGetValue(pr.DrawingId ?? "", out var drw);

                result.Add(new Profile
                {
                    Id = Guid.Parse(pr.Id),
                    ProjectId = Guid.Parse(pr.ProjectId),
                    Name = pr.Name,
                    Code = pr.Code,
                    Description = pr.Description,
                    MaterialId = string.IsNullOrEmpty(pr.MaterialId) ? (Guid?)null : Guid.Parse(pr.MaterialId),
                    DrawingId = string.IsNullOrEmpty(pr.DrawingId) ? (Guid?)null : Guid.Parse(pr.DrawingId),
                    DensityOverride = pr.DensityOverride,
                    CrossSectionArea = pr.CrossSectionArea,
                    CreatedAt = ParseDate(pr.CreatedAt),
                    UpdatedAt = ParseDate(pr.UpdatedAt),
                    MaterialName = mat?.Name,
                    MaterialDensity = mat != null ? (double?)mat.Density : null,
                    DrawingFilePath = drw?.FilePath,
                    DrawingName = drw?.Name
                });
            }
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
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

            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();
            var now = DateTime.UtcNow.ToString("o");

            if (p.Id == Guid.Empty)
            {
                p.Id = Guid.NewGuid();
                pf.Data.Profiles.Add(new ProfileRecord
                {
                    Id = p.Id.ToString(),
                    ProjectId = projectId,
                    Name = p.Name,
                    Code = p.Code,
                    Description = p.Description,
                    MaterialId = p.MaterialId?.ToString(),
                    DrawingId = p.DrawingId?.ToString(),
                    DensityOverride = p.DensityOverride,
                    CrossSectionArea = p.CrossSectionArea,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                var rec = pf.Data.Profiles.Find(r => r.Id == p.Id.ToString());
                if (rec != null)
                {
                    rec.Name = p.Name;
                    rec.Code = p.Code;
                    rec.Description = p.Description;
                    rec.MaterialId = p.MaterialId?.ToString();
                    rec.DrawingId = p.DrawingId?.ToString();
                    rec.DensityOverride = p.DensityOverride;
                    rec.CrossSectionArea = p.CrossSectionArea;
                    rec.UpdatedAt = now;
                }
            }
            Save(pf);
            return p.Id;
        }

        public void DeleteProfile(Guid id)
        {
            var pf = Load();
            pf.Data.Profiles.RemoveAll(pr => pr.Id == id.ToString());
            Save(pf);
        }

        public List<Drawing> GetProfileDrawings()
        {
            var result = new List<Drawing>();
            if (DatabaseService.CurrentProject == null) return result;
            var pf = Load();
            string projectId = DatabaseService.CurrentProject.Id.ToString();
            foreach (var d in pf.Data.Drawings)
            {
                if (d.ProjectId == projectId && d.DrawingType == DrawingTypes.Profile)
                    result.Add(MapDrawing(d));
            }
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // ====================================================================
        // NESTS
        // ====================================================================

        public NestRecord CreateNest(NestRecord nest)
        {
            if (DatabaseService.CurrentProject == null)
                throw new InvalidOperationException("No project open.");
            if (nest == null) throw new ArgumentNullException(nameof(nest));

            var pf = Load();
            if (pf.Data.Nests == null) pf.Data.Nests = new List<NestRecordJson>();

            if (nest.Id == Guid.Empty) nest.Id = Guid.NewGuid();
            if (nest.CreatedAt == default) nest.CreatedAt = DateTime.UtcNow;

            pf.Data.Nests.Add(MapNestToRecord(nest));
            Save(pf);
            return nest;
        }

        public List<NestRecord> GetNests()
        {
            var list = new List<NestRecord>();
            if (DatabaseService.CurrentProject == null) return list;

            var pf = Load();
            if (pf.Data.Nests == null) return list;

            string projectId = DatabaseService.CurrentProject.Id.ToString();
            foreach (var r in pf.Data.Nests)
            {
                if (r.ProjectId == projectId)
                    list.Add(MapNest(r));
            }
            list.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            return list;
        }

        public void UpdateNestSentToCut(Guid nestId, string dxfPath)
        {
            var pf = Load();
            if (pf.Data.Nests == null) return;
            string id = nestId.ToString();
            var rec = pf.Data.Nests.Find(n => n.Id == id);
            if (rec == null) return;
            rec.SentToCut    = true;
            rec.NestLocation = dxfPath;
            Save(pf);
        }

        public void UpdateNestCut(Guid nestId, bool cut, DateTime? cutDate)
        {
            var pf = Load();
            if (pf.Data.Nests == null) return;
            string id = nestId.ToString();
            var rec = pf.Data.Nests.Find(n => n.Id == id);
            if (rec == null) return;
            rec.Cut     = cut;
            rec.CutDate = cutDate.HasValue ? cutDate.Value.ToString("yyyy-MM-dd") : null;
            Save(pf);
        }

        private static NestRecordJson MapNestToRecord(NestRecord n)
        {
            return new NestRecordJson
            {
                Id             = n.Id.ToString(),
                ProjectId      = n.ProjectId.ToString(),
                DrawingId      = n.DrawingId.HasValue ? n.DrawingId.Value.ToString() : null,
                Name           = n.Name,
                CreatedAt      = n.CreatedAt.ToString("o"),
                MaterialName   = n.MaterialName,
                PlateCode      = n.PlateCode,
                PlateDimensions = n.PlateDimensions,
                PartCount      = n.PartCount,
                Efficiency     = n.Efficiency,
                SentToCut      = n.SentToCut,
                Cut            = n.Cut,
                CutDate        = n.CutDate.HasValue ? n.CutDate.Value.ToString("yyyy-MM-dd") : null,
                NestLocation   = n.NestLocation,
                DwgPath        = n.DwgPath
            };
        }

        private static NestRecord MapNest(NestRecordJson r)
        {
            return new NestRecord
            {
                Id             = Guid.Parse(r.Id),
                ProjectId      = Guid.Parse(r.ProjectId),
                DrawingId      = string.IsNullOrEmpty(r.DrawingId) ? (Guid?)null : Guid.Parse(r.DrawingId),
                Name           = r.Name,
                CreatedAt      = ParseDate(r.CreatedAt),
                MaterialName   = r.MaterialName,
                PlateCode      = r.PlateCode,
                PlateDimensions = r.PlateDimensions,
                PartCount      = r.PartCount,
                Efficiency     = r.Efficiency,
                SentToCut      = r.SentToCut,
                Cut            = r.Cut,
                CutDate        = string.IsNullOrEmpty(r.CutDate) ? (DateTime?)null : DateTime.Parse(r.CutDate),
                NestLocation   = r.NestLocation,
                DwgPath        = r.DwgPath
            };
        }

        // ====================================================================
        // HELPERS
        // ====================================================================

        private static DateTime ParseDate(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.UtcNow;
            return DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);
        }
    }
}
