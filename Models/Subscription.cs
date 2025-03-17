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
        public int MaxPrice { get; set; } = 999999999;
        public bool IncludePrivate { get; set; } = false;
        public bool IncludeOccupied { get; set; } = false;
        public bool IncludeProfessional { get; set; } = false;
        public bool IncludeBank { get; set; } = false;
    }
}
