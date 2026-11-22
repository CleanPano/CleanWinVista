using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace NovaTaskbar.App.Services;

/// <summary>Logger de arquivo mínimo (sem dependências extras). Um arquivo por dia, até 5 MB, retenção de 7 dias.
/// Registra só eventos do app (caminhos de apps fixados, erros) — nunca títulos de janelas nem conteúdo do usuário.</summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly BlockingCollection<string> _queue = new(4096);
    private readonly Thread _writer;
    private const long MaxBytesPerDay = 5 * 1024 * 1024;

    public FileLoggerProvider(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(_dir);
        Cleanup();
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "Nova log writer" };
        _writer.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Enqueue(string line) => _queue.TryAdd(line);

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                string file = Path.Combine(_dir, $"nova-{DateTime.Now:yyyyMMdd}.log");
                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxBytesPerDay) continue; // disco protegido contra laços de log
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void Cleanup()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_dir, "nova-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7)) File.Delete(f);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(2));
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        private readonly string _short = category[(category.LastIndexOf('.') + 1)..];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ')
              .Append(logLevel.ToString()[..4].ToUpperInvariant()).Append(' ')
              .Append(_short).Append(": ").Append(formatter(state, exception));
            if (exception != null) sb.Append(Environment.NewLine).Append(exception);
            owner.Enqueue(sb.ToString());
        }
    }
}
