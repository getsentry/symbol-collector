using System;
using Microsoft.Extensions.Logging;

namespace SymbolCollector.Console
{
    internal class LoggerAdapter<T> : ILogger<T>
    {
        private readonly LogLevel _minLogLevel;

        public LoggerAdapter(LogLevel minLogLevel = LogLevel.Trace) => _minLogLevel = minLogLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (logLevel == LogLevel.None || !IsEnabled(logLevel))
            {
                return;
            }

            var formatted = formatter?.Invoke(state, exception)
                            ?? exception?.ToString()
                            ?? state?.ToString()
                            ?? eventId.ToString();

            switch (logLevel)
            {
                default:
                    System.Console.WriteLine(formatted);
                    break;
            }
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLogLevel;

        public IDisposable BeginScope<TState>(TState state) => NoOpDisposable.Instance;

        private sealed class NoOpDisposable : IDisposable
        {
            internal static NoOpDisposable Instance { get; } = new NoOpDisposable();
            public void Dispose() { }
        }
    }
}
