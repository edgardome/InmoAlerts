using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InmoAlerts.Models
{
    public class Location
    {
        public int     Id                  { get; set; }
        public string  Name                { get; set; } = string.Empty;
        public string  Url                 { get; set; } = string.Empty;
        public string? RentUrl             { get; set; }
        /// <summary>How often (in minutes) to re-scan this zone. Default 2.</summary>
        public int     ScanIntervalMinutes { get; set; } = 2;
        /// <summary>If true, appends ?ordenado-por=fecha-publicacion-desc to the URL so newest listings appear first.</summary>
        public bool    SortByRecent        { get; set; } = true;
        /// <summary>Telegram chat ID to send instant alerts for this zone. Null = no Telegram alert.</summary>
        public string? TelegramChatId      { get; set; }
    }
}
