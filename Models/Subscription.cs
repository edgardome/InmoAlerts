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
    }
}
