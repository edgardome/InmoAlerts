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

            string createPropertiesTableQuery = @"CREATE TABLE IF NOT EXISTS Properties (
                    Id TEXT PRIMARY KEY,
                    Url TEXT,
                    Title TEXT,
                    Location TEXT,
                    Price INTEGER,
                    Currency TEXT,
                    Description TEXT,
                    IsOccupied BOOLEAN DEFAULT 0
                );";

            string createClientsTableQuery = @"
                CREATE TABLE IF NOT EXISTS Clients (
                    Id TEXT PRIMARY KEY,
                    Name TEXT,
                    Email TEXT,
                    HasActivePayment BOOLEAN DEFAULT 0,
                    SubscriptionEndDate TEXT
                );";

            string createSubscriptionsTableQuery = @"
                CREATE TABLE IF NOT EXISTS Subscriptions (
                    Id TEXT PRIMARY KEY,
                    ClientId TEXT,
                    Location TEXT,
                    MaxPrice INTEGER DEFAULT 1000000,
                    IncludeOccupied BOOLEAN DEFAULT 0,
                    IncludeProfessional BOOLEAN DEFAULT 0,
                    IncludeBank BOOLEAN DEFAULT 0,
                    IncludePrivate BOOLEAN DEFAULT 0,
                    FOREIGN KEY(ClientId) REFERENCES Clients(Id)
                );";

            using var command = new SqliteCommand(createPropertiesTableQuery, connection);
            command.ExecuteNonQuery();

            command.CommandText = createClientsTableQuery;
            command.ExecuteNonQuery();

            command.CommandText = createSubscriptionsTableQuery;
            command.ExecuteNonQuery();
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

        public static void InsertProperty(Property property)
        {
            if (PropertyExists(property.Id))
                return;

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string insertQuery = @"INSERT INTO Properties (Id, Url, Title, Location, Price, Currency, Description) 
                               VALUES (@Id, @Url, @Title, @Location, @Price, @Currency, @Description)";

            using var command = new SqliteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@Id", property.Id);
            command.Parameters.AddWithValue("@Url", property.Url);
            command.Parameters.AddWithValue("@Title", property.Title);
            command.Parameters.AddWithValue("@Location", property.Location);
            command.Parameters.AddWithValue("@Price", property.Price);
            command.Parameters.AddWithValue("@Currency", property.Currency);
            command.Parameters.AddWithValue("@Description", property.Description);

            command.ExecuteNonQuery();
        }

        public static void InsertClient(Client client)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string insertQuery = @"
                INSERT INTO Clients (
                    Id, Name, Email, HasActivePayment, SubscriptionEndDate
                ) VALUES (
                    @Id, @Name, @Email, @HasActivePayment, @SubscriptionEndDate
                )";

            using var command = new SqliteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@Id", client.Id);
            command.Parameters.AddWithValue("@Name", client.Name);
            command.Parameters.AddWithValue("@Email", client.Email);
            command.Parameters.AddWithValue("@HasActivePayment", client.HasActivePayment);
            command.Parameters.AddWithValue("@SubscriptionEndDate",
                (object?)client.SubscriptionEndDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value);

            command.ExecuteNonQuery();
        }

        public static void InsertSubscription(Subscription subscription)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string insertQuery = @"
                INSERT INTO Subscriptions (
                    Id, 
                    ClientId, 
                    Location, 
                    MaxPrice, 
                    IncludeOccupied, 
                    IncludeProfessional, 
                    IncludeBank,
                    IncludePrivate
                ) 
                VALUES (
                    @Id, 
                    @ClientId, 
                    @Location, 
                    @MaxPrice, 
                    @IncludeOccupied, 
                    @IncludeProfessional, 
                    @IncludeBank,
                    @IncludePrivate
                )";

            using var command = new SqliteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@Id", subscription.Id);
            command.Parameters.AddWithValue("@ClientId", subscription.ClientId);
            command.Parameters.AddWithValue("@Location", subscription.Location);
            command.Parameters.AddWithValue("@MaxPrice", subscription.MaxPrice);
            command.Parameters.AddWithValue("@IncludeOccupied", subscription.IncludeOccupied);
            command.Parameters.AddWithValue("@IncludeProfessional", subscription.IncludeProfessional);
            command.Parameters.AddWithValue("@IncludeBank", subscription.IncludeBank);
            command.Parameters.AddWithValue("@IncludePrivate", subscription.IncludePrivate);

            command.ExecuteNonQuery();
        }

        public static List<Subscription> GetSubscriptionsByClientId(string clientId)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string query = @"
                SELECT Id, ClientId, Location, MaxPrice, IncludeOccupied, IncludeProfessional, IncludeBank 
                FROM Subscriptions 
                WHERE ClientId = @ClientId";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@ClientId", clientId);

            using var reader = command.ExecuteReader();
            var subscriptions = new List<Subscription>();

            while (reader.Read())
            {
                subscriptions.Add(new Subscription
                {
                    Id = reader.GetString(0),
                    ClientId = reader.GetString(1),
                    Location = reader.GetString(2),
                    MaxPrice = reader.GetInt32(3),
                    IncludeOccupied = reader.GetBoolean(4),
                    IncludeProfessional = reader.GetBoolean(5),
                    IncludeBank = reader.GetBoolean(6)
                });
            }

            return subscriptions;
        }

        public static void ConfigureNewClient(Client client, List<Subscription> subscriptions)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Insertar cliente
                string insertClientQuery = @"INSERT INTO Clients (Id, Name, Email) 
                             VALUES (@Id, @Name, @Email)";
                using var clientCommand = new SqliteCommand(insertClientQuery, connection, transaction);
                clientCommand.Parameters.AddWithValue("@Id", client.Id);
                clientCommand.Parameters.AddWithValue("@Name", client.Name);
                clientCommand.Parameters.AddWithValue("@Email", client.Email);
                clientCommand.ExecuteNonQuery();

                // Insertar suscripciones
                string insertSubscriptionQuery = @"
                    INSERT INTO Subscriptions (
                        Id, ClientId, Location, MaxPrice, IncludeOccupied, IncludeProfessional, IncludeBank
                    ) VALUES (
                        @Id, @ClientId, @Location, @MaxPrice, @IncludeOccupied, @IncludeProfessional, @IncludeBank
                    )";
                foreach (var subscription in subscriptions)
                {
                    using var subscriptionCommand = new SqliteCommand(insertSubscriptionQuery, connection, transaction);
                    subscriptionCommand.Parameters.AddWithValue("@Id", subscription.Id);
                    subscriptionCommand.Parameters.AddWithValue("@ClientId", subscription.ClientId);
                    subscriptionCommand.Parameters.AddWithValue("@Location", subscription.Location);
                    subscriptionCommand.Parameters.AddWithValue("@MaxPrice", subscription.MaxPrice);
                    subscriptionCommand.Parameters.AddWithValue("@IncludeOccupied", subscription.IncludeOccupied);
                    subscriptionCommand.Parameters.AddWithValue("@IncludeProfessional", subscription.IncludeProfessional);
                    subscriptionCommand.Parameters.AddWithValue("@IncludeBank", subscription.IncludeBank);
                    subscriptionCommand.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }

        public static List<string> GetEmailsForLocation(Property property)
        {
            List<string> emails = new List<string>();
            try
            {
                bool isOccupied = Utils.IsPropertyOccupied(property.Description);

                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                string query = @"
                    SELECT DISTINCT c.Email 
                    FROM Clients c 
                    JOIN Subscriptions s ON c.Id = s.ClientId 
                    WHERE s.Location = @Location
                    AND @Price <= s.MaxPrice
                    AND c.HasActivePayment = 1  -- Verificar estado de pago
                    AND (@IsOccupied = 0 OR s.IncludeOccupied = 1)
                    AND (
                        (@IsBank = 1 AND s.IncludeBank = 1)
                        OR
                        (@IsBank = 0 AND (@IsProfessional = 0 OR s.IncludeProfessional = 1))
                    )";

                using var command = new SqliteCommand(query, connection);
                command.Parameters.AddWithValue("@Location", property.Location);
                command.Parameters.AddWithValue("@Price", property.Price);
                command.Parameters.AddWithValue("@IsOccupied", isOccupied ? 1 : 0);
                command.Parameters.AddWithValue("@IsProfessional", property.IsProfessional ? 1 : 0);
                command.Parameters.AddWithValue("@IsBank", property.IsBank ? 1 : 0);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    emails.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al obtener correos de la base de datos: {ex.Message}");
            }
            return emails;
        }

        public static List<Client> GetAllClients()
        {
            var clients = new List<Client>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var command = new SqliteCommand(@"
                    SELECT Id, Name, Email, HasActivePayment, SubscriptionEndDate 
                    FROM Clients", connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var subscriptionEndDateStr = reader["SubscriptionEndDate"].ToString();
                            DateTime? subscriptionEndDate = !string.IsNullOrEmpty(subscriptionEndDateStr)
                                ? DateTime.Parse(subscriptionEndDateStr)
                                : null;

                            clients.Add(new Client
                            {
                                Id = reader["Id"].ToString() ?? string.Empty,
                                Name = reader["Name"].ToString() ?? string.Empty,
                                Email = reader["Email"].ToString() ?? string.Empty,
                                HasActivePayment = Convert.ToBoolean(reader["HasActivePayment"]),
                                SubscriptionEndDate = subscriptionEndDate
                            });
                        }
                    }
                }
            }
            return clients;
        }

        public static List<Subscription> GetAllSubscriptions()
        {
            var subscriptions = new List<Subscription>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var command = new SqliteCommand(@"
            SELECT Id, ClientId, Location, MaxPrice, IncludeOccupied, 
                   IncludeProfessional, IncludeBank, IncludePrivate 
            FROM Subscriptions", connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            subscriptions.Add(new Subscription
                            {
                                Id = reader["Id"].ToString() ?? string.Empty,
                                ClientId = reader["ClientId"].ToString() ?? string.Empty,
                                Location = reader["Location"].ToString() ?? string.Empty,
                                MaxPrice = Convert.ToInt32(reader["MaxPrice"]),
                                IncludeOccupied = Convert.ToBoolean(reader["IncludeOccupied"]),
                                IncludeProfessional = Convert.ToBoolean(reader["IncludeProfessional"]),
                                IncludeBank = Convert.ToBoolean(reader["IncludeBank"]),
                                IncludePrivate = Convert.ToBoolean(reader["IncludePrivate"])
                            });
                        }
                    }
                }
            }
            return subscriptions;
        }

        public static void UpdateSubscriptionStatus(string clientId)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string selectQuery = @"
                SELECT HasActivePayment, SubscriptionEndDate 
                FROM Clients 
                WHERE Id = @ClientId";

            using var selectCommand = new SqliteCommand(selectQuery, connection);
            selectCommand.Parameters.AddWithValue("@ClientId", clientId);

            using var reader = selectCommand.ExecuteReader();
            if (reader.Read())
            {
                bool currentStatus = Convert.ToBoolean(reader["HasActivePayment"]);
                string? endDateStr = reader["SubscriptionEndDate"].ToString();

                if (currentStatus && !string.IsNullOrEmpty(endDateStr))
                {
                    var endDate = DateTime.Parse(endDateStr);

                    if (DateTime.Now > endDate)
                    {
                        // La suscripción ha expirado, actualizamos el estado
                        string updateQuery = @"
                            UPDATE Clients 
                            SET HasActivePayment = 0,
                                SubscriptionEndDate = NULL
                            WHERE Id = @ClientId";

                        using var updateCommand = new SqliteCommand(updateQuery, connection);
                        updateCommand.Parameters.AddWithValue("@ClientId", clientId);
                        updateCommand.ExecuteNonQuery();
                    }
                }
            }
        }

        public static void ActivateSubscription(string clientId, int days)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string updateQuery = @"
                UPDATE Clients 
                SET HasActivePayment = 1,
                    SubscriptionEndDate = @EndDate
                WHERE Id = @ClientId";

            using var command = new SqliteCommand(updateQuery, connection);
            command.Parameters.AddWithValue("@ClientId", clientId);
            command.Parameters.AddWithValue("@EndDate", DateTime.Now.AddDays(days).ToString("yyyy-MM-dd HH:mm:ss"));

            command.ExecuteNonQuery();
        }
    }
}
