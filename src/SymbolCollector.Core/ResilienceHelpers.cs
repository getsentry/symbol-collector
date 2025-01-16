using Microsoft.Extensions.Http.Resilience;
using Polly;
using Sentry;

namespace SymbolCollector.Core;

public static class ResilienceHelpers
{
    /// <summary>
    /// Creates an instance of the default retry strategy we use for Sentry requests.
    /// </summary>
    /// <param name="addBreadcrumb">
    /// An optional callback that can be used to fake/mock creating a breadcrumb (for testing).
    /// If none is provided then the static SentrySdk.AddBreadcrumb method will be used instead.
    /// </param>
    /// <returns>A new <see cref="HttpRetryStrategyOptions"/> instance</returns>
    public static HttpRetryStrategyOptions SentryRetryStrategy(Action<string, Dictionary<string, string>>? addBreadcrumb = null) =>
        new HttpRetryStrategyOptions
        {
            BackoffType = DelayBackoffType.Exponential,
#if RELEASE
            // TODO: Until a proper re-entrancy is built in the clients, let it retry for a while
            MaxRetryAttempts = 6,
#else
            MaxRetryAttempts = 3,
#endif
            OnRetry = arguments =>
            {
                var data = new Dictionary<string, string>
                {
                    { "RetryDelay", arguments.RetryDelay.ToString() },
                    { "AttemptNumber", arguments.AttemptNumber.ToString() },
                    { "Duration", arguments.Duration.ToString() },
                    { "ThreadId", Thread.CurrentThread.ManagedThreadId.ToString() }
                };
                if (arguments.Outcome.Exception is { } e)
                {
                    data.Add("exception", e.ToString());
                }

                addBreadcrumb ??= (m, d) => SentrySdk.AddBreadcrumb(m, data: d);
                addBreadcrumb(
                    $"Waiting {arguments.RetryDelay} following attempt {arguments.AttemptNumber} failed HTTP request.",
                    data);
                return ValueTask.CompletedTask;
            }
        };
}
