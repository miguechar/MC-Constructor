using Npgsql;
using System;
using System.Collections.Generic;

namespace FirstAcadPlugin
{
    /// <summary>
    /// Represents a project from the database.
    /// </summary>
    public class Project
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Represents a drawing part to be saved to the database.
    /// </summary>
    public class DrawingPart
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public string DrawingName { get; set; }
        public string PartName { get; set; }
        public string ObjectHandle { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Service for connecting to PostgreSQL database and managing drawing parts.
    /// </summary>
    public static class DatabaseService
    {
        private static string _connectionString = "Host=localhost;Port=5432;Database=your_database;Username=postgres;Password=your_password";

        public static Project CurrentProject { get; private set; }

        public static void SetConnectionString(string connectionString)
        {
            _connectionString = connectionString;
        }

        public static void SetConnectionString(string host, int port, string database, string username, string password)
        {
            _connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password}";
        }

        public static bool TestConnection(out string errorMessage)
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
            catch (System.Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static List<Project> GetProjects()
        {
            var projects = new List<Project>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new NpgsqlCommand("SELECT id, name, description FROM public.projects ORDER BY name", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        projects.Add(new Project
                        {
                            Id = reader.GetGuid(0),
                            Name = reader.GetString(1),
                            Description = reader.IsDBNull(2) ? "" : reader.GetString(2)
                        });
                    }
                }
            }

            return projects;
        }

        public static void SetCurrentProject(Project project)
        {
            CurrentProject = project;
        }

        public static Guid SaveDrawingPart(string drawingName, string partName, string objectHandle)
        {
            if (CurrentProject == null)
                throw new InvalidOperationException("No project is currently open. Use MC_OPEN_PROJECT first.");

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                // Check if exists
                using (var checkCmd = new NpgsqlCommand(
                    @"SELECT id FROM public.drawing_parts
                      WHERE project_id = @projectId AND drawing_name = @drawingName AND object_handle = @objectHandle", conn))
                {
                    checkCmd.Parameters.AddWithValue("projectId", CurrentProject.Id);
                    checkCmd.Parameters.AddWithValue("drawingName", drawingName);
                    checkCmd.Parameters.AddWithValue("objectHandle", objectHandle ?? "");

                    var existingId = checkCmd.ExecuteScalar();

                    if (existingId != null && existingId != DBNull.Value)
                    {
                        // Update
                        using (var updateCmd = new NpgsqlCommand(
                            @"UPDATE public.drawing_parts SET part_name = @partName, updated_at = NOW()
                              WHERE id = @id RETURNING id", conn))
                        {
                            updateCmd.Parameters.AddWithValue("id", (Guid)existingId);
                            updateCmd.Parameters.AddWithValue("partName", partName);
                            return (Guid)updateCmd.ExecuteScalar();
                        }
                    }
                }

                // Insert new
                using (var insertCmd = new NpgsqlCommand(
                    @"INSERT INTO public.drawing_parts (project_id, drawing_name, part_name, object_handle)
                      VALUES (@projectId, @drawingName, @partName, @objectHandle) RETURNING id", conn))
                {
                    insertCmd.Parameters.AddWithValue("projectId", CurrentProject.Id);
                    insertCmd.Parameters.AddWithValue("drawingName", drawingName);
                    insertCmd.Parameters.AddWithValue("partName", partName);
                    insertCmd.Parameters.AddWithValue("objectHandle", objectHandle ?? "");
                    return (Guid)insertCmd.ExecuteScalar();
                }
            }
        }

        public static int SaveAllDrawingParts(List<DrawingPart> parts)
        {
            if (CurrentProject == null)
                throw new InvalidOperationException("No project is currently open.");

            int savedCount = 0;

            foreach (var part in parts)
            {
                try
                {
                    SaveDrawingPart(part.DrawingName, part.PartName, part.ObjectHandle);
                    savedCount++;
                }
                catch { }
            }

            return savedCount;
        }

        public static List<DrawingPart> GetPartsForCurrentProject()
        {
            if (CurrentProject == null)
                return new List<DrawingPart>();

            return GetPartsForProject(CurrentProject.Id);
        }

        public static List<DrawingPart> GetPartsForProject(Guid projectId)
        {
            var parts = new List<DrawingPart>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new NpgsqlCommand(
                    @"SELECT id, project_id, drawing_name, part_name, object_handle, created_at, updated_at
                      FROM public.drawing_parts WHERE project_id = @projectId ORDER BY drawing_name, part_name", conn))
                {
                    cmd.Parameters.AddWithValue("projectId", projectId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            parts.Add(new DrawingPart
                            {
                                Id = reader.GetGuid(0),
                                ProjectId = reader.GetGuid(1),
                                DrawingName = reader.GetString(2),
                                PartName = reader.GetString(3),
                                ObjectHandle = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                CreatedAt = reader.GetDateTime(5),
                                UpdatedAt = reader.GetDateTime(6)
                            });
                        }
                    }
                }
            }

            return parts;
        }
    }
}
