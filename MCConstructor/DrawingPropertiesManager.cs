using Autodesk.AutoCAD.DatabaseServices;
using System;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCConstructor
{
    /// <summary>
    /// Reads and writes the "MC Constructor" drawing properties (type,
    /// discipline, project link) on a drawing. Properties are stored as
    /// custom properties in the .dwg file's SummaryInfo, so they survive
    /// independently of the database and can be inspected from File > Drawing
    /// Properties > Custom in vanilla AutoCAD.
    /// </summary>
    public static class DrawingPropertiesManager
    {
        // Custom property keys. Keep "MC_" prefix to avoid clashing with
        // user-defined drawing properties.
        public const string KeyDrawingType    = "MC_DrawingType";
        public const string KeyDiscipline     = "MC_Discipline";
        public const string KeyProjectId      = "MC_ProjectId";
        public const string KeyProjectName    = "MC_ProjectName";
        public const string KeyDrawingId      = "MC_DrawingId";
        public const string KeyDescription    = "MC_Description";
        /// <summary>Absolute path to the base drawing overlaid as an XRef in this drawing.</summary>
        public const string KeyBaseDrawingPath = "MC_BaseDrawingPath";

        /// <summary>
        /// Read the drawing properties out of the active document.
        /// Missing values come back as null/empty strings.
        /// </summary>
        public static DrawingPropertiesData GetCurrent()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return new DrawingPropertiesData();
            return Read(doc.Database);
        }

        public static DrawingPropertiesData Read(Database db)
        {
            var data = new DrawingPropertiesData();
            if (db == null) return data;

            var summary = db.SummaryInfo;
            data.DrawingType     = TryGetCustom(summary, KeyDrawingType);
            data.Discipline      = TryGetCustom(summary, KeyDiscipline);
            data.ProjectName     = TryGetCustom(summary, KeyProjectName);
            data.Description     = TryGetCustom(summary, KeyDescription);
            data.BaseDrawingPath = TryGetCustom(summary, KeyBaseDrawingPath);

            var pid = TryGetCustom(summary, KeyProjectId);
            if (Guid.TryParse(pid, out var pg)) data.ProjectId = pg;

            var did = TryGetCustom(summary, KeyDrawingId);
            if (Guid.TryParse(did, out var dg)) data.DrawingId = dg;

            return data;
        }

        /// <summary>
        /// Write the given properties to the active document's SummaryInfo.
        /// Caller is responsible for saving the drawing if desired.
        /// </summary>
        public static void SetCurrent(DrawingPropertiesData data)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active document.");

            using (doc.LockDocument())
            {
                Write(doc.Database, data);
            }
        }

        public static void Write(Database db, DrawingPropertiesData data)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (data == null) throw new ArgumentNullException(nameof(data));

            // SummaryInfo is read-only; the SDK forces us to use the builder
            // and assign a new instance.
            var builder = new DatabaseSummaryInfoBuilder(db.SummaryInfo);

            SetCustom(builder, KeyDrawingType,    data.DrawingType);
            SetCustom(builder, KeyDiscipline,     data.Discipline);
            SetCustom(builder, KeyProjectId,      data.ProjectId?.ToString());
            SetCustom(builder, KeyProjectName,    data.ProjectName);
            SetCustom(builder, KeyDrawingId,      data.DrawingId?.ToString());
            SetCustom(builder, KeyDescription,    data.Description);
            SetCustom(builder, KeyBaseDrawingPath, data.BaseDrawingPath);

            db.SummaryInfo = builder.ToDatabaseSummaryInfo();
        }

        private static string TryGetCustom(DatabaseSummaryInfo info, string key)
        {
            if (info == null || info.CustomProperties == null) return null;
            try
            {
                var enumerator = info.CustomProperties;
                while (enumerator.MoveNext())
                {
                    if (string.Equals((string)enumerator.Key, key, StringComparison.OrdinalIgnoreCase))
                        return enumerator.Value as string;
                }
            }
            catch { }
            return null;
        }

        private static void SetCustom(DatabaseSummaryInfoBuilder builder, string key, string value)
        {
            // Builder.CustomPropertyTable is an IDictionary; set null/empty by
            // removing so we don't pollute the file with empty entries.
            if (builder.CustomPropertyTable.Contains(key))
                builder.CustomPropertyTable.Remove(key);

            if (!string.IsNullOrEmpty(value))
                builder.CustomPropertyTable.Add(key, value);
        }
    }

    /// <summary>
    /// Plain-object representation of the MC Constructor drawing properties.
    /// </summary>
    public class DrawingPropertiesData
    {
        public string DrawingType { get; set; }
        public string Discipline { get; set; }
        public Guid?  ProjectId { get; set; }
        public string ProjectName { get; set; }
        public Guid?  DrawingId { get; set; }
        public string Description { get; set; }
        /// <summary>Absolute path to the base drawing overlaid as an XRef, or null.</summary>
        public string BaseDrawingPath { get; set; }

        public bool HasType => !string.IsNullOrEmpty(DrawingType);
    }
}
