using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Idealista.Models
{
    public class Location
    {
        public int     Id      { get; set; }
        public string  Name    { get; set; } = string.Empty;
        public string  Url     { get; set; } = string.Empty;
        public string? RentUrl { get; set; }
    }
}
