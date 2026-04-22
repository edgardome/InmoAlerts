using Idealista.Models;

namespace Idealista.Services
{
    public class ScraperState
    {
        public bool IsRunning { get; set; } = false;
        public int CycleCount { get; set; } = 0;
        public int NewLeadsSession { get; set; } = 0;
        public int EmailsSentSession { get; set; } = 0;
        public DateTime? LastRunTime { get; set; } = null;
        public DateTime? NextCycleTime { get; set; } = null;
        public string CurrentLocation { get; set; } = "En espera";
        public int PropertyCount { get; set; } = 0;
        public Property? LastProperty { get; set; } = null;
    }
}
