using InmoAlerts.Models;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using System.Security.Cryptography;

namespace InmoAlerts.Helpers
{
    public static class DatabaseHelper
    {
        private static readonly string ProjectRoot =
            Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.Parent?.Parent?.FullName
            ?? throw new InvalidOperationException("No se pudo encontrar la raíz del proyecto");

        private static readonly string ConnectionString =
            $"Data Source={Path.Combine(ProjectRoot, "properties.db")}";

        // ── Init ────────────────────────────────────────────────────────────────

        public static void InitializeDatabase()
        {
            using var connection = new SqliteConnection(ConnectionString);
            SQLitePCL.Batteries.Init();
            connection.Open();

            // Properties table
            ExecuteNonQuery(connection, @"
                CREATE TABLE IF NOT EXISTS Properties (
                    Id          TEXT PRIMARY KEY,
                    Url         TEXT,
                    Title       TEXT,
                    Location    TEXT,
                    Province    TEXT,
                    Price       INTEGER,
                    Currency    TEXT,
                    Description TEXT,
                    IsProfessional BOOLEAN DEFAULT 0,
                    IsBank         BOOLEAN DEFAULT 0,
                    ListingType    TEXT    DEFAULT 'Venta',
                    CreatedAt      TEXT    DEFAULT CURRENT_TIMESTAMP
                );");

            // Users table
            ExecuteNonQuery(connection, @"
                CREATE TABLE IF NOT EXISTS Users (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username     TEXT NOT NULL UNIQUE,
                    PasswordHash TEXT NOT NULL,
                    Role         TEXT NOT NULL DEFAULT 'user',
                    CreatedAt    TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );");

            // Locations table
            ExecuteNonQuery(connection, @"
                CREATE TABLE IF NOT EXISTS Locations (
                    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name      TEXT NOT NULL,
                    Url       TEXT NOT NULL,
                    RentUrl   TEXT,
                    SortOrder INTEGER NOT NULL DEFAULT 0
                );");

            MigrateProperties(connection);
            MigrateUsers(connection);
            SeedUsers(connection);
            MigrateLocationsFromJson(connection);

            Console.WriteLine("✓ Base de datos inicializada correctamente.");
        }

        private static void ExecuteNonQuery(SqliteConnection conn, string sql)
        {
            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        // ── Properties migration (legacy columns) ────────────────────────────────

        private static void MigrateProperties(SqliteConnection connection)
        {
            try
            {
                using var cmd = new SqliteCommand("PRAGMA table_info(Properties)", connection);
                using var reader = cmd.ExecuteReader();

                bool hasIsProfessional = false, hasIsBank = false,
                     hasCreatedAt = false, hasListingType = false;

                while (reader.Read())
                {
                    var col = reader.GetString(1);
                    if (col == "IsProfessional") hasIsProfessional = true;
                    if (col == "IsBank")         hasIsBank = true;
                    if (col == "CreatedAt")      hasCreatedAt = true;
                    if (col == "ListingType")    hasListingType = true;
                }
                reader.Close();

                if (!hasIsProfessional) ExecuteNonQuery(connection, "ALTER TABLE Properties ADD COLUMN IsProfessional BOOLEAN DEFAULT 0");
                if (!hasIsBank)         ExecuteNonQuery(connection, "ALTER TABLE Properties ADD COLUMN IsBank BOOLEAN DEFAULT 0");
                if (!hasCreatedAt)      ExecuteNonQuery(connection, "ALTER TABLE Properties ADD COLUMN CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP");
                if (!hasListingType)    ExecuteNonQuery(connection, "ALTER TABLE Properties ADD COLUMN ListingType TEXT DEFAULT 'Venta'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error durante la migración de propiedades: {ex.Message}");
            }
        }

        // ── Users migration (legacy columns) ────────────────────────────────────

        private static void MigrateUsers(SqliteConnection connection)
        {
            try
            {
                using var cmd = new SqliteCommand("PRAGMA table_info(Users)", connection);
                using var reader = cmd.ExecuteReader();
                bool hasAlertEmails = false;
                bool hasAllowedProvinces = false;
                bool hasAlertVenta = false;
                bool hasAlertAlquiler = false;
                while (reader.Read())
                {
                    var col = reader.GetString(1);
                    if (col == "AlertEmails")      hasAlertEmails      = true;
                    if (col == "AllowedProvinces") hasAllowedProvinces = true;
                    if (col == "AlertVenta")       hasAlertVenta       = true;
                    if (col == "AlertAlquiler")    hasAlertAlquiler    = true;
                }
                reader.Close();

                if (!hasAlertEmails)
                    ExecuteNonQuery(connection, "ALTER TABLE Users ADD COLUMN AlertEmails TEXT NOT NULL DEFAULT '[]'");
                if (!hasAllowedProvinces)
                    ExecuteNonQuery(connection, "ALTER TABLE Users ADD COLUMN AllowedProvinces TEXT NOT NULL DEFAULT '[]'");
                if (!hasAlertVenta)
                    ExecuteNonQuery(connection, "ALTER TABLE Users ADD COLUMN AlertVenta INTEGER NOT NULL DEFAULT 1");
                if (!hasAlertAlquiler)
                    ExecuteNonQuery(connection, "ALTER TABLE Users ADD COLUMN AlertAlquiler INTEGER NOT NULL DEFAULT 1");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error migrando tabla Users: {ex.Message}");
            }
        }

        // ── Seed default users if none exist ────────────────────────────────────

        private static void SeedUsers(SqliteConnection connection)
        {
            using var countCmd = new SqliteCommand("SELECT COUNT(*) FROM Users", connection);
            var count = Convert.ToInt32(countCmd.ExecuteScalar());
            if (count > 0) return;

            // Try to migrate credentials from appsettings.json if present
            string? adminUser = null, adminPass = null, guestUser = null, guestPass = null;
            try
            {
                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (File.Exists(settingsPath))
                {
                    var json   = File.ReadAllText(settingsPath);
                    var doc    = Newtonsoft.Json.Linq.JObject.Parse(json);
                    var dash   = doc["DashboardSettings"];
                    adminUser  = dash?["Username"]?.ToString();
                    adminPass  = dash?["Password"]?.ToString();
                    guestUser  = dash?["UserUsername"]?.ToString();
                    guestPass  = dash?["UserPassword"]?.ToString();
                }
            }
            catch { /* ignore – fall back to defaults */ }

            CreateUser(connection, adminUser ?? "admin", adminPass ?? "admin", "admin");
            if (!string.IsNullOrWhiteSpace(guestUser))
                CreateUser(connection, guestUser, guestPass ?? "user", "user");
            else
                CreateUser(connection, "user", "user", "user");

            Console.WriteLine("✓ Usuarios por defecto creados (admin/admin · user/user).");
        }

        // ── Migrate locations.json → Locations table ────────────────────────────

        private static void MigrateLocationsFromJson(SqliteConnection connection)
        {
            using var countCmd = new SqliteCommand("SELECT COUNT(*) FROM Locations", connection);
            if (Convert.ToInt32(countCmd.ExecuteScalar()) > 0) return;

            var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "locations.json");
            if (!File.Exists(jsonPath)) return;

            try
            {
                var json  = File.ReadAllText(jsonPath);
                var data  = JsonConvert.DeserializeObject<LocationsConfig>(json);
                if (data?.Locations == null || data.Locations.Count == 0) return;

                int order = 0;
                foreach (var loc in data.Locations)
                {
                    using var cmd = new SqliteCommand(
                        "INSERT INTO Locations (Name, Url, RentUrl, SortOrder) VALUES (@n, @u, @r, @s)",
                        connection);
                    cmd.Parameters.AddWithValue("@n", loc.Name);
                    cmd.Parameters.AddWithValue("@u", loc.Url);
                    cmd.Parameters.AddWithValue("@r", (object?)loc.RentUrl ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@s", order++);
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine($"✓ {data.Locations.Count} ubicaciones migradas desde locations.json.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error migrando locations.json: {ex.Message}");
            }
        }

        // ── Password utilities ───────────────────────────────────────────────────

        public static string HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
            return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        public static bool VerifyPassword(string password, string stored)
        {
            var parts = stored.Split(':');
            if (parts.Length != 2) return false;
            try
            {
                var salt       = Convert.FromBase64String(parts[0]);
                var storedHash = Convert.FromBase64String(parts[1]);
                var hash       = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
                return CryptographicOperations.FixedTimeEquals(hash, storedHash);
            }
            catch { return false; }
        }

        // ── User CRUD ────────────────────────────────────────────────────────────

        private static void CreateUser(SqliteConnection connection, string username, string password, string role)
        {
            using var cmd = new SqliteCommand(
                "INSERT OR IGNORE INTO Users (Username, PasswordHash, Role) VALUES (@u, @p, @r)",
                connection);
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@p", HashPassword(password));
            cmd.Parameters.AddWithValue("@r", role);
            cmd.ExecuteNonQuery();
        }

        public static AppUser? ValidateUser(string username, string password)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqliteCommand(
                "SELECT Id, Username, PasswordHash, Role, AlertEmails, AllowedProvinces, CreatedAt, AlertVenta, AlertAlquiler FROM Users WHERE Username = @u COLLATE NOCASE",
                connection);
            cmd.Parameters.AddWithValue("@u", username);
            using var reader = cmd.ExecuteReader();

            if (!reader.Read()) return null;

            var user = ReadUser(reader);
            return VerifyPassword(password, user.PasswordHash) ? user : null;
        }

        public static List<AppUser> GetAllUsers()
        {
            var users = new List<AppUser>();
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqliteCommand(
                "SELECT Id, Username, PasswordHash, Role, AlertEmails, AllowedProvinces, CreatedAt, AlertVenta, AlertAlquiler FROM Users ORDER BY Id",
                connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) users.Add(ReadUser(reader));
            return users;
        }

        public static AppUser? GetUserById(int id)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqliteCommand(
                "SELECT Id, Username, PasswordHash, Role, AlertEmails, AllowedProvinces, CreatedAt, AlertVenta, AlertAlquiler FROM Users WHERE Id = @id",
                connection);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadUser(reader) : null;
        }

        public static (bool ok, string error) CreateUser(string username, string password, string role)
        {
            if (string.IsNullOrWhiteSpace(username)) return (false, "El nombre de usuario no puede estar vacío");
            if (string.IsNullOrWhiteSpace(password)) return (false, "La contraseña no puede estar vacía");
            if (role != "admin" && role != "user")   return (false, "Rol inválido");

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var checkCmd = new SqliteCommand(
                "SELECT COUNT(*) FROM Users WHERE Username = @u COLLATE NOCASE", connection);
            checkCmd.Parameters.AddWithValue("@u", username);
            if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                return (false, "Ya existe un usuario con ese nombre");

            using var cmd = new SqliteCommand(
                "INSERT INTO Users (Username, PasswordHash, Role) VALUES (@u, @p, @r)", connection);
            cmd.Parameters.AddWithValue("@u", username.Trim());
            cmd.Parameters.AddWithValue("@p", HashPassword(password));
            cmd.Parameters.AddWithValue("@r", role);
            cmd.ExecuteNonQuery();
            return (true, string.Empty);
        }

        public static (bool ok, string error) UpdateUser(int id, string? newUsername, string? newPassword, string? newRole, List<string>? newAlertEmails, List<string>? newAllowedProvinces, bool? newAlertVenta, bool? newAlertAlquiler, string currentUsername)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var user = GetUserById(id);
            if (user == null) return (false, "Usuario no encontrado");

            // Safety: can't demote/rename the last admin
            if (newRole == "user" && user.Role == "admin")
            {
                using var adminCount = new SqliteCommand(
                    "SELECT COUNT(*) FROM Users WHERE Role = 'admin'", connection);
                if (Convert.ToInt32(adminCount.ExecuteScalar()) <= 1)
                    return (false, "No puedes cambiar el rol del único administrador");
            }

            if (!string.IsNullOrWhiteSpace(newUsername) && newUsername != user.Username)
            {
                using var checkCmd = new SqliteCommand(
                    "SELECT COUNT(*) FROM Users WHERE Username = @u COLLATE NOCASE AND Id != @id", connection);
                checkCmd.Parameters.AddWithValue("@u", newUsername);
                checkCmd.Parameters.AddWithValue("@id", id);
                if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                    return (false, "Ya existe un usuario con ese nombre");
            }

            var finalUsername     = string.IsNullOrWhiteSpace(newUsername) ? user.Username : newUsername.Trim();
            var finalHash         = string.IsNullOrWhiteSpace(newPassword)  ? user.PasswordHash : HashPassword(newPassword);
            var finalRole         = string.IsNullOrWhiteSpace(newRole)      ? user.Role : newRole;
            var finalEmails       = newAlertEmails != null
                ? System.Text.Json.JsonSerializer.Serialize(newAlertEmails.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList())
                : System.Text.Json.JsonSerializer.Serialize(user.AlertEmails);
            var finalProvinces    = newAllowedProvinces != null
                ? System.Text.Json.JsonSerializer.Serialize(newAllowedProvinces.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList())
                : System.Text.Json.JsonSerializer.Serialize(user.AllowedProvinces);
            var finalAlertVenta    = newAlertVenta    ?? user.AlertVenta;
            var finalAlertAlquiler = newAlertAlquiler ?? user.AlertAlquiler;

            using var updateCmd = new SqliteCommand(
                "UPDATE Users SET Username = @u, PasswordHash = @p, Role = @r, AlertEmails = @e, AllowedProvinces = @ap, AlertVenta = @av, AlertAlquiler = @aa WHERE Id = @id", connection);
            updateCmd.Parameters.AddWithValue("@u",  finalUsername);
            updateCmd.Parameters.AddWithValue("@p",  finalHash);
            updateCmd.Parameters.AddWithValue("@r",  finalRole);
            updateCmd.Parameters.AddWithValue("@e",  finalEmails);
            updateCmd.Parameters.AddWithValue("@ap", finalProvinces);
            updateCmd.Parameters.AddWithValue("@av", finalAlertVenta    ? 1 : 0);
            updateCmd.Parameters.AddWithValue("@aa", finalAlertAlquiler ? 1 : 0);
            updateCmd.Parameters.AddWithValue("@id", id);
            updateCmd.ExecuteNonQuery();
            return (true, string.Empty);
        }

        public static (bool ok, string error) DeleteUser(int id, string currentUsername)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var user = GetUserById(id);
            if (user == null) return (false, "Usuario no encontrado");
            if (user.Username.Equals(currentUsername, StringComparison.OrdinalIgnoreCase))
                return (false, "No puedes eliminar tu propia cuenta");

            if (user.Role == "admin")
            {
                using var adminCount = new SqliteCommand(
                    "SELECT COUNT(*) FROM Users WHERE Role = 'admin'", connection);
                if (Convert.ToInt32(adminCount.ExecuteScalar()) <= 1)
                    return (false, "No puedes eliminar el único administrador");
            }

            using var cmd = new SqliteCommand("DELETE FROM Users WHERE Id = @id", connection);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            return (true, string.Empty);
        }

        private static AppUser ReadUser(SqliteDataReader r)
        {
            List<string> ParseJson(int col) {
                var json = r.IsDBNull(col) ? "[]" : r.GetString(col);
                try   { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
                catch { return new(); }
            }
            return new AppUser
            {
                Id               = r.GetInt32(0),
                Username         = r.GetString(1),
                PasswordHash     = r.GetString(2),
                Role             = r.GetString(3),
                AlertEmails      = ParseJson(4),
                AllowedProvinces = ParseJson(5),
                CreatedAt        = r.IsDBNull(6) ? string.Empty : r.GetString(6),
                AlertVenta       = r.IsDBNull(7) || r.GetInt32(7) != 0,
                AlertAlquiler    = r.IsDBNull(8) || r.GetInt32(8) != 0,
            };
        }

        public static (bool ok, string error) UpdateUserEmails(int id, List<string> emails)
        {
            emails = emails.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();
            var json = System.Text.Json.JsonSerializer.Serialize(emails);
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var cmd = new SqliteCommand(
                "UPDATE Users SET AlertEmails = @e WHERE Id = @id", connection);
            cmd.Parameters.AddWithValue("@e",  json);
            cmd.Parameters.AddWithValue("@id", id);
            var rows = cmd.ExecuteNonQuery();
            return rows > 0 ? (true, string.Empty) : (false, "Usuario no encontrado");
        }

        public static List<string> GetAllAlertEmails()
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var cmd = new SqliteCommand("SELECT AlertEmails FROM Users", connection);
            using var reader = cmd.ExecuteReader();
            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var json = reader.IsDBNull(0) ? "[]" : reader.GetString(0);
                try
                {
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new();
                    foreach (var e in list.Where(x => !string.IsNullOrWhiteSpace(x)))
                        all.Add(e.Trim());
                }
                catch { }
            }
            return all.ToList();
        }

        /// <summary>
        /// Returns alert emails only for users who:
        /// - Have no zone restriction or their zone matches the property's province/location
        /// - Have the listing type (Venta/Alquiler) enabled in their preferences
        /// </summary>
        public static List<string> GetAlertEmailsForProperty(string province, string location, string listingType)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var cmd = new SqliteCommand("SELECT AlertEmails, AllowedProvinces, AlertVenta, AlertAlquiler FROM Users", connection);
            using var reader = cmd.ExecuteReader();
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var emailsJson    = reader.IsDBNull(0) ? "[]" : reader.GetString(0);
                var provincesJson = reader.IsDBNull(1) ? "[]" : reader.GetString(1);
                var alertVenta    = reader.IsDBNull(2) || reader.GetInt32(2) != 0;
                var alertAlquiler = reader.IsDBNull(3) || reader.GetInt32(3) != 0;
                try
                {
                    // Check listing type preference
                    bool typeAllowed = listingType.Equals("Alquiler", StringComparison.OrdinalIgnoreCase)
                        ? alertAlquiler
                        : alertVenta;
                    if (!typeAllowed) continue;

                    var emails    = System.Text.Json.JsonSerializer.Deserialize<List<string>>(emailsJson)    ?? new();
                    var provinces = System.Text.Json.JsonSerializer.Deserialize<List<string>>(provincesJson) ?? new();

                    // Empty AllowedProvinces = all zones
                    bool zoneAllowed = provinces.Count == 0
                        || provinces.Any(p =>
                            p.Equals(province, StringComparison.OrdinalIgnoreCase) ||
                            p.Equals(location, StringComparison.OrdinalIgnoreCase));

                    if (zoneAllowed)
                        foreach (var e in emails.Where(x => !string.IsNullOrWhiteSpace(x)))
                            result.Add(e.Trim());
                }
                catch { }
            }
            return result.ToList();
        }

        // ── Locations CRUD ───────────────────────────────────────────────────────

        public static List<Location> GetAllLocations()
        {
            var locs = new List<Location>();
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqliteCommand(
                "SELECT Id, Name, Url, RentUrl FROM Locations ORDER BY SortOrder, Id",
                connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                locs.Add(new Location
                {
                    Id      = reader.GetInt32(0),
                    Name    = reader.GetString(1),
                    Url     = reader.GetString(2),
                    RentUrl = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
            }
            return locs;
        }

        public static (bool ok, string error, Location? created) AddLocation(string name, string url, string? rentUrl)
        {
            if (string.IsNullOrWhiteSpace(name)) return (false, "El nombre es obligatorio", null);
            if (string.IsNullOrWhiteSpace(url))  return (false, "La URL es obligatoria", null);

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var maxCmd = new SqliteCommand("SELECT COALESCE(MAX(SortOrder),0)+1 FROM Locations", connection);
            var sort = Convert.ToInt32(maxCmd.ExecuteScalar());

            using var cmd = new SqliteCommand(
                "INSERT INTO Locations (Name, Url, RentUrl, SortOrder) VALUES (@n, @u, @r, @s); SELECT last_insert_rowid();",
                connection);
            cmd.Parameters.AddWithValue("@n", name.Trim());
            cmd.Parameters.AddWithValue("@u", url.Trim());
            cmd.Parameters.AddWithValue("@r", string.IsNullOrWhiteSpace(rentUrl) ? DBNull.Value : (object)rentUrl.Trim());
            cmd.Parameters.AddWithValue("@s", sort);
            var newId = Convert.ToInt32(cmd.ExecuteScalar());

            return (true, string.Empty, new Location { Id = newId, Name = name, Url = url, RentUrl = rentUrl });
        }

        public static (bool ok, string error) DeleteLocation(int id)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqliteCommand("DELETE FROM Locations WHERE Id = @id", connection);
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() > 0 ? (true, string.Empty) : (false, "Ubicación no encontrada");
        }

        // ── Properties ───────────────────────────────────────────────────────────

        public static bool PropertyExists(string id)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqliteCommand("SELECT COUNT(1) FROM Properties WHERE Id = @Id", connection);
            cmd.Parameters.AddWithValue("@Id", id);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public static int? GetPropertyPrice(string id)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqliteCommand("SELECT Price FROM Properties WHERE Id = @Id", connection);
            cmd.Parameters.AddWithValue("@Id", id);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        public static void UpdatePropertyPrice(string id, int newPrice)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqliteCommand("UPDATE Properties SET Price = @Price WHERE Id = @Id", connection);
            cmd.Parameters.AddWithValue("@Price", newPrice);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
            Console.WriteLine($"🔄 Precio actualizado para {id}: {newPrice}€");
        }

        public static void InsertProperty(Property property)
        {
            if (PropertyExists(property.Id)) return;

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqliteCommand(@"
                INSERT INTO Properties (Id, Url, Title, Location, Province, Price, Currency, Description, IsProfessional, IsBank, ListingType)
                VALUES (@Id, @Url, @Title, @Location, @Province, @Price, @Currency, @Description, @IsProfessional, @IsBank, @ListingType)",
                connection);
            cmd.Parameters.AddWithValue("@Id",            property.Id);
            cmd.Parameters.AddWithValue("@Url",           property.Url);
            cmd.Parameters.AddWithValue("@Title",         property.Title);
            cmd.Parameters.AddWithValue("@Location",      property.Location);
            cmd.Parameters.AddWithValue("@Province",      property.Province);
            cmd.Parameters.AddWithValue("@Price",         property.Price);
            cmd.Parameters.AddWithValue("@Currency",      property.Currency);
            cmd.Parameters.AddWithValue("@Description",   property.Description);
            cmd.Parameters.AddWithValue("@IsProfessional", property.IsProfessional);
            cmd.Parameters.AddWithValue("@IsBank",        property.IsBank);
            cmd.Parameters.AddWithValue("@ListingType",   property.ListingType);
            cmd.ExecuteNonQuery();
            Console.WriteLine($"💾 Guardada [{property.ListingType}]: {property.Title} - {property.Price}{property.Currency}");
        }

        public static List<Property> GetAllProperties()
        {
            var properties = new List<Property>();
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd    = new SqliteCommand("SELECT * FROM Properties ORDER BY CreatedAt DESC", connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                properties.Add(new Property
                {
                    Id            = reader["Id"].ToString()       ?? string.Empty,
                    Url           = reader["Url"].ToString()      ?? string.Empty,
                    Title         = reader["Title"].ToString()    ?? string.Empty,
                    Location      = reader["Location"].ToString() ?? string.Empty,
                    Province      = reader["Province"].ToString() ?? string.Empty,
                    Price         = Convert.ToInt32(reader["Price"]),
                    Currency      = reader["Currency"].ToString() ?? string.Empty,
                    Description   = reader["Description"].ToString() ?? string.Empty,
                    IsProfessional = Convert.ToBoolean(reader["IsProfessional"]),
                    IsBank        = Convert.ToBoolean(reader["IsBank"]),
                    ListingType   = reader["ListingType"]?.ToString() ?? "Venta"
                });
            }
            return properties;
        }

        public static int GetPropertyCount()
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqliteCommand("SELECT COUNT(*) FROM Properties", connection);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }
}
