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
        public bool IncludeOccupied { get; set; } = true;
        public bool IncludeProfessional { get; set; } = true;
        public bool IncludeBank { get; set; } = true;
    }
}
