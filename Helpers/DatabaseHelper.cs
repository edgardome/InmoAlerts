using Idealista.Models;
using Microsoft.Data.Sqlite;
using static Idealista.Models.Models;

namespace Idealista.Helpers
{
    public static class DatabaseHelper
    {
    private static readonly string ProjectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.Parent?.Parent?.FullName ?? "";
    private static readonly string ConnectionString = $"Data Source={Path.Combine(ProjectRoot, "properties.db")};";


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
                                    Description TEXT
                                );";

            string createClientsTableQuery = @"CREATE TABLE IF NOT EXISTS Clients (
                                    Id TEXT PRIMARY KEY,
                                    Name TEXT,
                                    Email TEXT
                                );";

            string createSubscriptionsTableQuery = @"CREATE TABLE IF NOT EXISTS Subscriptions (
                                    Id TEXT PRIMARY KEY,
                                    ClientId TEXT,
                                    City TEXT,
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

            string insertQuery = @"INSERT INTO Clients (Id, Name, Email) 
                           VALUES (@Id, @Name, @Email)";

            using var command = new SqliteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@Id", client.Id);
            command.Parameters.AddWithValue("@Name", client.Name);
            command.Parameters.AddWithValue("@Email", client.Email);

            command.ExecuteNonQuery();
        }

        public static void InsertSubscription(Subscription subscription)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string insertQuery = @"INSERT INTO Subscriptions (Id, ClientId, City) 
                           VALUES (@Id, @ClientId, @City)";

            using var command = new SqliteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@Id", subscription.Id);
            command.Parameters.AddWithValue("@ClientId", subscription.ClientId);
            command.Parameters.AddWithValue("@City", subscription.City);

            command.ExecuteNonQuery();
        }

        public static List<Subscription> GetSubscriptionsByClientId(string clientId)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            string query = "SELECT Id, ClientId, City FROM Subscriptions WHERE ClientId = @ClientId";
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
                    City = reader.GetString(2)
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
                string insertSubscriptionQuery = @"INSERT INTO Subscriptions (Id, ClientId, City) 
                                           VALUES (@Id, @ClientId, @City)";
                foreach (var subscription in subscriptions)
                {
                    using var subscriptionCommand = new SqliteCommand(insertSubscriptionQuery, connection, transaction);
                    subscriptionCommand.Parameters.AddWithValue("@Id", subscription.Id);
                    subscriptionCommand.Parameters.AddWithValue("@ClientId", subscription.ClientId);
                    subscriptionCommand.Parameters.AddWithValue("@City", subscription.City);
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

        public static List<string> GetEmailsForCity(string city)
        {
            List<string> emails = new List<string>();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    string query = @"SELECT DISTINCT Clients.Email 
                                      FROM Clients 
                                      JOIN Subscriptions ON Clients.Id = Subscriptions.ClientId 
                                      WHERE Subscriptions.City = @City";
                    using (var command = new SqliteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@City", city);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                emails.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al obtener correos de la base de datos: {ex.Message}");
            }
            return emails;
        }
    }
}
