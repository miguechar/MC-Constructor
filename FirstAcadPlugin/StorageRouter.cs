using System;

namespace FirstAcadPlugin
{
    internal static class StorageRouter
    {
        private static IStorageProvider _provider;

        public static string ActiveMode { get; private set; }

        public static bool IsInitialized => _provider != null;

        public static IStorageProvider Provider
        {
            get
            {
                if (_provider == null)
                    throw new InvalidOperationException(
                        "No storage provider initialized. Open a project with MCOpenProject or MCCreateProject first.");
                return _provider;
            }
        }

        public static void UseNpgsql(string connectionString)
        {
            _provider = new NpgsqlStorageProvider(connectionString);
            ActiveMode = "postgres";
        }

        public static void UseJson(string filePath)
        {
            _provider = new JsonStorageProvider(filePath);
            ActiveMode = "json";
        }

        // For migration commands that need a temporary Npgsql provider
        // without changing the active session provider.
        internal static NpgsqlStorageProvider BuildNpgsqlProvider(string connectionString)
        {
            return new NpgsqlStorageProvider(connectionString);
        }

        // Returns the connection string if in postgres mode; null otherwise.
        internal static string GetNpgsqlConnectionString()
        {
            return (_provider as NpgsqlStorageProvider)?.ConnectionString;
        }

        // Returns the pro.json file path if in json mode; null otherwise.
        internal static string GetJsonFilePath()
        {
            return (_provider as JsonStorageProvider)?.FilePath;
        }
    }
}
