namespace InmoAlerts.Models
{
    public class AppUser
    {
        public int          Id               { get; set; }
        public string       Username         { get; set; } = string.Empty;
        public string       PasswordHash     { get; set; } = string.Empty;
        public string       Role             { get; set; } = "user";
        public List<string> AlertEmails      { get; set; } = new();
        public List<string> AllowedProvinces { get; set; } = new();
        public bool         AlertVenta       { get; set; } = true;
        public bool         AlertAlquiler    { get; set; } = true;
        public string       CreatedAt        { get; set; } = string.Empty;
    }
}
