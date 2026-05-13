using System.Collections.Generic;

namespace FirstAcadPlugin
{
    // Root object serialized to/from pro.json
    internal class ProjectFile
    {
        public int SchemaVersion { get; set; } = 1;
        // "json" or "postgres"
        public string StorageMode { get; set; }
        public ProjectFileProject Project { get; set; }
        public ProjectFileDatabaseConfig Database { get; set; }
        public ProjectFileData Data { get; set; }
    }

    internal class ProjectFileProject
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Directory { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }

    internal class ProjectFileDatabaseConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Database { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }

    internal class ProjectFileData
    {
        public List<DrawingRecord> Drawings { get; set; } = new List<DrawingRecord>();
        public List<DrawingPartRecord> Parts { get; set; } = new List<DrawingPartRecord>();
        public List<MaterialRecord> Materials { get; set; } = new List<MaterialRecord>();
        public List<MaterialPlateRecord> MaterialPlates { get; set; } = new List<MaterialPlateRecord>();
        public List<ProfileRecord> Profiles { get; set; } = new List<ProfileRecord>();
    }

    internal class DrawingRecord
    {
        public string Id { get; set; }
        public string ProjectId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string DrawingType { get; set; }
        public string Discipline { get; set; }
        public string FilePath { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }

    internal class DrawingPartRecord
    {
        public string Id { get; set; }
        public string ProjectId { get; set; }
        public string PartName { get; set; }
        public string PartDescription { get; set; }
        public string SourceDrawingName { get; set; }
        public string SourceObjectHandle { get; set; }
        public string ParentPartId { get; set; }
        public bool IsOriginal { get; set; }
        public bool IsOverride { get; set; }
        public string GeometryData { get; set; }
        public string GeometryType { get; set; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double PositionZ { get; set; }
        public double RotationX { get; set; }
        public double RotationY { get; set; }
        public double RotationZ { get; set; }
        public double ScaleX { get; set; }
        public double ScaleY { get; set; }
        public double ScaleZ { get; set; }
        public string LayerName { get; set; }
        public int ColorIndex { get; set; }
        public string MaterialName { get; set; }
        public double? BBoxMinX { get; set; }
        public double? BBoxMinY { get; set; }
        public double? BBoxMinZ { get; set; }
        public double? BBoxMaxX { get; set; }
        public double? BBoxMaxY { get; set; }
        public double? BBoxMaxZ { get; set; }
        public string MaterialId { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public int Version { get; set; }
    }

    internal class MaterialRecord
    {
        public string Id { get; set; }
        public string ProjectId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double Density { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }

    internal class MaterialPlateRecord
    {
        public string Id { get; set; }
        public string MaterialId { get; set; }
        public string Code { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Thickness { get; set; }
        public string Description { get; set; }
        public bool IsDefault { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }

    internal class ProfileRecord
    {
        public string Id { get; set; }
        public string ProjectId { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public string MaterialId { get; set; }
        public string DrawingId { get; set; }
        public double? DensityOverride { get; set; }
        public double? CrossSectionArea { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }
}
