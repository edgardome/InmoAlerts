using Idealista.Configuration;
using Idealista.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Idealista.Helpers
{
    public static class DatabaseHelper
    {
        private static readonly string ProjectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.Parent?.Parent?.FullName
            ?? throw new InvalidOperationException("No se pudo encontrar la raíz del proyecto");

        private static readonly string ConnectionString = $"Data Source={Path.Combine(ProjectRoot, "properties.db")}";

        public static void InitializeDatabase()
        {
            using var connection = new SqliteConnection(ConnectionString);
            SQLitePCL.Batteries.Init();
            connection.Open();

            // Crear tabla si no existe
            string createPropertiesTableQuery = @"
                CREATE TABLE IF NOT EXISTS Properties (
                    Id TEXT PRIMARY KEY,
                    Url TEXT,
                    Title TEXT,
                    Location TEXT,
                    Province TEXT,
                    Price INTEGER,
                    Currency TEXT,
                    Description TEXT,
                    IsProfessional BOOLEAN DEFAULT 0,
                    IsBank BOOLEAN DEFAULT 0,
                    ListingType TEXT DEFAULT 'Venta',
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                );";

            using var command = new SqliteCommand(createPropertiesTableQuery, connection);
            command.ExecuteNonQuery();

            // Verificar y añadir columnas faltantes si la tabla ya existía
            MigrateDatabase(connection);
            
            Console.WriteLine("✓ Base de datos inicializada correctamente.");
        }

        private static void MigrateDatabase(SqliteConnection connection)
        {
            try
            {
                var checkColumnQuery = "PRAGMA table_info(Properties)";
                using var command = new SqliteCommand(checkColumnQuery, connection);
                using var reader = command.ExecuteReader();

                bool hasIsProfessional = false;
                bool hasIsBank = false;
                bool hasCreatedAt = false;
                bool hasListingType = false;

                while (reader.Read())
                {
                    string columnName = reader.GetString(1);
                    if (columnName == "IsProfessional") hasIsProfessional = true;
                    if (columnName == "IsBank") hasIsBank = true;
                    if (columnName == "CreatedAt") hasCreatedAt = true;
                    if (columnName == "ListingType") hasListingType = true;
                }

                reader.Close();

                if (!hasIsProfessional)
                {
                    Console.WriteLine("🔄 Añadiendo columna IsProfessional...");
                    using var cmd = new SqliteCommand("ALTER TABLE Properties ADD COLUMN IsProfessional BOOLEAN DEFAULT 0", connection);
                    cmd.ExecuteNonQuery();
                }
                if (!hasIsBank)
                {
                    Console.WriteLine("🔄 Añadiendo columna IsBank...");
                    using var cmd = new SqliteCommand("ALTER TABLE Properties ADD COLUMN IsBank BOOLEAN DEFAULT 0", connection);
                    cmd.ExecuteNonQuery();
                }
                if (!hasCreatedAt)
                {
                    Console.WriteLine("🔄 Añadiendo columna CreatedAt...");
                    using var cmd = new SqliteCommand("ALTER TABLE Properties ADD COLUMN CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP", connection);
                    cmd.ExecuteNonQuery();
                }
                if (!hasListingType)
                {
                    Console.WriteLine("🔄 Añadiendo columna ListingType...");
                    using var cmd = new SqliteCommand("ALTER TABLE Properties ADD COLUMN ListingType TEXT DEFAULT 'Venta'", connection);
                    cmd.ExecuteNonQuery();
                }

                if (!hasIsProfessional || !hasIsBank || !hasCreatedAt || !hasListingType)
                    Console.WriteLine("✓ Migración de base de datos completada.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error durante la migración: {ex.Message}");
            }
        }

        public static bool PropertyExists(string id)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string query = "SELECT COUNT(1) FROM Properties WHERE Id = @Id";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        /// <summary>Returns the stored price for a property, or null if not found.</summary>
        public static int? GetPropertyPrice(string id)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string query = "SELECT Price FROM Properties WHERE Id = @Id";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            var result = command.ExecuteScalar();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        public static void UpdatePropertyPrice(string id, int newPrice)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string query = "UPDATE Properties SET Price = @Price WHERE Id = @Id";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@Price", newPrice);
            command.Parameters.AddWithValue("@Id", id);
            command.ExecuteNonQuery();
            Console.WriteLine($"🔄 Precio actualizado para {id}: {newPrice}€");
        }

        public static void InsertProperty(Property property)
        {
            if (PropertyExists(property.Id))
            {
                return;
            }

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string insertQuery = @"
                INSERT INTO Properties (
                    Id, Url, Title, Location, Province, Price, Currency, Description, IsProfessional, IsBank, ListingType
                ) VALUES (
                    @Id, @Url, @Title, @Location, @Province, @Price, @Currency, @Description, @IsProfessional, @IsBank, @ListingType
                )";

            using var command = new SqliteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@Id", property.Id);
            command.Parameters.AddWithValue("@Url", property.Url);
            command.Parameters.AddWithValue("@Title", property.Title);
            command.Parameters.AddWithValue("@Location", property.Location);
            command.Parameters.AddWithValue("@Province", property.Province);
            command.Parameters.AddWithValue("@Price", property.Price);
            command.Parameters.AddWithValue("@Currency", property.Currency);
            command.Parameters.AddWithValue("@Description", property.Description);
            command.Parameters.AddWithValue("@IsProfessional", property.IsProfessional);
            command.Parameters.AddWithValue("@IsBank", property.IsBank);
            command.Parameters.AddWithValue("@ListingType", property.ListingType);

            command.ExecuteNonQuery();
            Console.WriteLine($"💾 Guardada [{property.ListingType}]: {property.Title} - {property.Price}{property.Currency}");
        }

        public static List<Property> GetAllProperties()
        {
            var properties = new List<Property>();
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string query = "SELECT * FROM Properties ORDER BY CreatedAt DESC";
            using var command = new SqliteCommand(query, connection);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                properties.Add(new Property
                {
                    Id = reader["Id"].ToString() ?? string.Empty,
                    Url = reader["Url"].ToString() ?? string.Empty,
                    Title = reader["Title"].ToString() ?? string.Empty,
                    Location = reader["Location"].ToString() ?? string.Empty,
                    Province = reader["Province"].ToString() ?? string.Empty,
                    Price = Convert.ToInt32(reader["Price"]),
                    Currency = reader["Currency"].ToString() ?? string.Empty,
                    Description = reader["Description"].ToString() ?? string.Empty,
                    IsProfessional = Convert.ToBoolean(reader["IsProfessional"]),
                    IsBank = Convert.ToBoolean(reader["IsBank"]),
                    ListingType = reader["ListingType"]?.ToString() ?? "Venta"
                });
            }

            return properties;
        }

        public static int GetPropertyCount()
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string query = "SELECT COUNT(*) FROM Properties";
            using var command = new SqliteCommand(query, connection);

            return Convert.ToInt32(command.ExecuteScalar());
        }
    }
}
