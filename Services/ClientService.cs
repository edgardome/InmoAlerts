using Idealista.Helpers;
using Idealista.Models;

namespace Idealista.Services
{
    public class ClientService
    {
        public async Task<List<Client>> GetAllClientsAsync()
        {
            return await Task.Run(() => DatabaseHelper.GetAllClients());
        }

        public async Task InsertClientAsync(Client client)
        {
            await Task.Run(() => DatabaseHelper.InsertClient(client));
        }

        public async Task ConfigureNewClientAsync(Client client, List<Subscription> subscriptions)
        {
            await Task.Run(() => DatabaseHelper.ConfigureNewClient(client, subscriptions));
        }
    }
}