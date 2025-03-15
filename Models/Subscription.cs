using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Idealista.Models
{
    public class Subscription
    {
        public required string Id { get; set; }
        public required string ClientId { get; set; }
        public required string Location { get; set; }
        public int MaxPrice { get; set; } = 999999999; // Precio máximo por defecto
        public bool IncludeOccupied { get; set; } = true; // Por defecto incluir ocupadas
    }
}
