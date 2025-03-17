using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Idealista.Models
{
    public class Client
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string Email { get; set; }
        public bool HasActivePayment { get; set; } = false;
        public DateTime? SubscriptionEndDate { get; set; } = null;
    }
}
