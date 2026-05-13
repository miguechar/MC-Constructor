using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Creates or overwrites a pro.json file for a project.
    /// </summary>
    internal static class ProjectFileWriter
    {
        private static readonly JsonSerializerOptions _opts = new JsonSerializerOptions { WriteIndented = true };

        /// <summary>
        /// Write a new pro.json at <paramref name="path"/> for the given project.
        /// When storageMode is "postgres", the database section is populated from
        /// the connection string and the data section is empty.
        /// When storageMode is "json", the database section is omitted.
        /// </summary>
        public static void WriteNew(string path, Project project, string storageMode, string connectionString)
        {
            var pf = new ProjectFile
            {
                SchemaVersion = 1,
                StorageMode = storageMode,
                Project = new ProjectFileProject
                {
                    Id = project.Id.ToString(),
                    Name = project.Name,
                    Description = project.Description,
                    Directory = project.Directory,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                },
                Data = new ProjectFileData()
            };

            if (storageMode == "postgres" && !string.IsNullOrEmpty(connectionString))
                pf.Database = ParseConnectionString(connectionString);

            SaveAtomic(path, pf);
        }

        /// <summary>
        /// Load an existing pro.json and return it (for reading credentials or project info).
        /// Returns null if the file does not exist or cannot be parsed.
        /// </summary>
        public static ProjectFile TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<ProjectFile>(json, _opts);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Build a Npgsql connection string from the database section of a pro.json.
        /// </summary>
        public static string BuildConnectionString(ProjectFileDatabaseConfig db)
        {
            if (db == null) return null;
            return $"Host={db.Host};Port={db.Port};Database={db.Database};Username={db.Username};Password={db.Password}";
        }

        private static void SaveAtomic(string path, ProjectFile pf)
        {
            string tmp = path + ".tmp";
            string json = JsonSerializer.Serialize(pf, _opts);
            File.WriteAllText(tmp, json, Encoding.UTF8);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }

        private static ProjectFileDatabaseConfig ParseConnectionString(string cs)
        {
            // Parse "Host=h;Port=p;Database=d;Username=u;Password=pw" format
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
