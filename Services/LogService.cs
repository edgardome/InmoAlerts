using System.Threading.Channels;

namespace Idealista.Services
{
    public class LogEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class LogService
    {
        private const int MaxBufferSize = 500;
        private readonly List<LogEntry> _buffer = new();
        private readonly List<Channel<LogEntry>> _subscribers = new();
        private readonly object _lock = new();

        public void Log(string message)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Message = message
            };

            lock (_lock)
            {
                _buffer.Add(entry);
                if (_buffer.Count > MaxBufferSize)
                    _buffer.RemoveAt(0);

                foreach (var ch in _subscribers)
                    ch.Writer.TryWrite(entry);
            }
        }

        public IReadOnlyList<LogEntry> GetBuffer()
        {
            lock (_lock)
                return _buffer.ToList();
        }

        public IAsyncEnumerable<LogEntry> SubscribeAsync(CancellationToken ct)
        {
            var channel = Channel.CreateUnbounded<LogEntry>();

            lock (_lock)
            {
                // Send existing buffer to new subscriber
                foreach (var entry in _buffer)
                    channel.Writer.TryWrite(entry);

                _subscribers.Add(channel);
            }

            ct.Register(() =>
            {
                lock (_lock)
                {
                    _subscribers.Remove(channel);
                    channel.Writer.TryComplete();
                }
            });

            return channel.Reader.ReadAllAsync(ct);
        }
    }
}
