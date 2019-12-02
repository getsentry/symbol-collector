using System;
using Android.Util;
using Microsoft.Extensions.Logging;
using AndroidLog = Android.Util.Log;

namespace SymbolCollector.Android
{
    internal class LoggerAdapter<T> : ILogger<T>
    {
        private static readonly string Tag = typeof(T).Name;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var formatted = formatter?.Invoke(state, exception)
                            ?? exception?.ToString()
                            ?? state?.ToString()
                            ?? eventId.ToString();

            AndroidLog.WriteLine(logLevel.ToLogPriority(), Tag, formatted);
        }

        public bool IsEnabled(LogLevel logLevel) => AndroidLog.IsLoggable(Tag, logLevel.ToLogPriority());

        public IDisposable BeginScope<TState>(TState state) => NoOpDisposable.Instance;

        private sealed class NoOpDisposable : IDisposable
        {
            internal static NoOpDisposable Instance { get; } = new NoOpDisposable();
            public void Dispose() {}
        }
    }

    internal static class LevelAdapter
    {
        public static LogPriority ToLogPriority(this LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace: return LogPriority.Verbose;
                case LogLevel.Debug: return LogPriority.Debug;
                case LogLevel.Information: return LogPriority.Info;
                case LogLevel.Warning: return LogPriority.Warn;
                case LogLevel.Error: return LogPriority.Error;
                case LogLevel.Critical: return LogPriority.Error;
                case LogLevel.None: return (LogPriority)(-1);
                default: return (LogPriority)level;
            }
        }
    }
}
