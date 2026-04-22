namespace Idealista.Configuration
{
    public class ScraperSettings
    {
        public int DelayBetweenRequests { get; set; } = 2000;
        public int MaxRetries { get; set; } = 3;
        public string UserAgent { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 30;
        public bool UseProxy { get; set; } = false;
        public string ProxyUrl { get; set; } = string.Empty;
        public int MinutesBetweenCycles { get; set; } = 5;
    }
}