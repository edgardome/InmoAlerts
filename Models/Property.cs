namespace InmoAlerts.Models
{
    public class Property
    {
        public bool IsProfessional { get; set; } = false;
        public bool IsBank { get; set; } = false;
        public string Id { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public int Price { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ListingType { get; set; } = "Venta"; // "Venta" | "Alquiler"
    }
}
