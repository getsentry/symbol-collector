using System;
using Android.Util;
using Microsoft.Extensions.Logging;
using Sentry;
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
            var formatted = formatter?.Invoke(state, exception);
            if (formatted != null && exception != null)
            {
                formatted = $"{formatted} ex: {exception}";
            }
            formatted ??= exception?.ToString()
                          ?? state?.ToString()
                          ?? eventId.ToString();

            // TODO: Use Sentry.Extensions.Logging instead
            SentrySdk.AddBreadcrumb(formatted, Tag);
            if (logLevel >= LogLevel.Error)
            {
                SentrySdk.CaptureEvent(new SentryEvent(exception)
                {
                    Message = formatter?.Invoke(state, exception!),
                    Logger = typeof(Logger<T>).Name
                });
            }
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
        public static LogPriority ToLogPriority(this LogLevel level) =>
            level switch
            {
                LogLevel.Trace => LogPriority.Verbose,
                LogLevel.Debug => LogPriority.Debug,
                LogLevel.Information => LogPriority.Info,
                LogLevel.Warning => LogPriority.Warn,
                LogLevel.Error => LogPriority.Error,
                LogLevel.Critical => LogPriority.Error,
                LogLevel.None => (LogPriority)(-1),
                _ => (LogPriority)level
            };
    }
}
