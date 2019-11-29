using System;
using Microsoft.Extensions.Logging;

namespace SymbolCollector.Console
{
    public class LoggerAdapter<T> : ILogger<T>
    {
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (logLevel == LogLevel.None)
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

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable BeginScope<TState>(TState state) => NoOpDisposable.Instance;

        private sealed class NoOpDisposable : IDisposable
        {
            internal static NoOpDisposable Instance { get; } = new NoOpDisposable();
            public void Dispose() { }
        }
    }
}
