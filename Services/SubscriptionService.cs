using Idealista.Helpers;
using Idealista.Models;

namespace Idealista.Services
{
    public class SubscriptionService
    {
        public async Task<List<Subscription>> GetAllSubscriptionsAsync()
        {
            return await Task.Run(() => DatabaseHelper.GetAllSubscriptions());
        }

        public async Task InsertSubscriptionAsync(Subscription subscription)
        {
            await Task.Run(() => DatabaseHelper.InsertSubscription(subscription));
        }

        public async Task<List<Subscription>> GetSubscriptionsByClientIdAsync(string clientId)
        {
            return await Task.Run(() => DatabaseHelper.GetSubscriptionsByClientId(clientId));
        }

        public async Task<List<string>> GetEmailsForLocationAsync(string location)
        {
            return await Task.Run(() => DatabaseHelper.GetEmailsForLocation(location));
        }
    }
}