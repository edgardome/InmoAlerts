using Idealista.Helpers;
using Idealista.Models;

namespace Idealista.Services
{
    public class PropertyService
    {
        public async Task<bool> PropertyExistsAsync(string id)
        {
            return await Task.Run(() => DatabaseHelper.PropertyExists(id));
        }

        public async Task InsertPropertyAsync(Property property)
        {
            await Task.Run(() => DatabaseHelper.InsertProperty(property));
        }

        public async Task ProcessPropertyAsync(Property property)
        {
            if (!await PropertyExistsAsync(property.Id))
            {
                await InsertPropertyAsync(property);
                await EmailHelper.SendPropertyEmail(property);
            }
        }
    }
}