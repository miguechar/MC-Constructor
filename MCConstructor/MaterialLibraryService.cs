using System;
using System.Collections.Generic;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Facade for CRUD of material library entities.
    /// Delegates to the active IStorageProvider via StorageRouter.
    /// </summary>
    public static class MaterialLibraryService
    {
        public static List<Material> GetMaterials() => StorageRouter.Provider.GetMaterials();

        public static Guid SaveMaterial(Material m) => StorageRouter.Provider.SaveMaterial(m);

        public static void DeleteMaterial(Guid id) => StorageRouter.Provider.DeleteMaterial(id);

        public static List<MaterialPlate> GetPlates(Guid materialId) => StorageRouter.Provider.GetPlates(materialId);

        public static List<MaterialPlate> GetPlatesForCurrentProject() => StorageRouter.Provider.GetPlatesForCurrentProject();

        public static Guid SavePlate(MaterialPlate p) => StorageRouter.Provider.SavePlate(p);

        public static void DeletePlate(Guid id) => StorageRouter.Provider.DeletePlate(id);

        public static List<Profile> GetProfiles() => StorageRouter.Provider.GetProfiles();

        public static Guid SaveProfile(Profile p) => StorageRouter.Provider.SaveProfile(p);

        public static void DeleteProfile(Guid id) => StorageRouter.Provider.DeleteProfile(id);

        public static List<Drawing> GetProfileDrawings() => StorageRouter.Provider.GetProfileDrawings();
    }
}
