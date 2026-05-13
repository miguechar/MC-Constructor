using System;
using System.Collections.Generic;

namespace MCConstructor
{
    public interface IStorageProvider
    {
        bool TestConnection(out string errorMessage);

        // Projects
        List<Project> GetProjects();
        Project CreateProject(string name, string description, string directory);
        bool ProjectNameExists(string name);

        // Drawings
        Drawing CreateDrawing(Guid id, string name, string drawingType, string discipline, string filePath, string description);
        List<Drawing> GetDrawings();
        bool DrawingNameExistsInCurrentProject(string name);
        Drawing FindNestTemplate();
        Drawing GetDrawingByPath(string filePath);
        void UpdateDrawingProperties(Guid drawingId, string drawingType, string discipline, string description);

        // Parts
        Guid SavePart(DrawingPart part);
        List<DrawingPart> GetOriginalParts();
        DrawingPart GetPartById(Guid partId);
        List<DrawingPart> GetPartReferences(Guid parentPartId);
        List<DrawingPart> GetOverriddenParts();
        void OverridePart(Guid partId, DrawingPart updatedPart);
        void ApplyOverrideToOriginal(Guid overridePartId);
        Guid SavePartReference(DrawingPart part, Guid parentPartId);

        // Materials
        List<Material> GetMaterials();
        Guid SaveMaterial(Material m);
        void DeleteMaterial(Guid id);

        // Material Plates
        List<MaterialPlate> GetPlates(Guid materialId);
        List<MaterialPlate> GetPlatesForCurrentProject();
        Guid SavePlate(MaterialPlate p);
        void DeletePlate(Guid id);

        // Profiles
        List<Profile> GetProfiles();
        Guid SaveProfile(Profile p);
        void DeleteProfile(Guid id);
        List<Drawing> GetProfileDrawings();
    }
}
