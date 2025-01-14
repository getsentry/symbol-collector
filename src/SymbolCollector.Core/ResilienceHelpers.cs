using Microsoft.Extensions.Http.Resilience;
using Polly;
using Sentry;

namespace SymbolCollector.Core;

public static class ResilienceHelpers
{
    public static HttpRetryStrategyOptions SentryRetryStrategy() =>
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

                SentrySdk.AddBreadcrumb(
                    $"Waiting {arguments.RetryDelay} following attempt {arguments.AttemptNumber} failed HTTP request.",
                    data: data);
                return ValueTask.CompletedTask;
            }
        };
}
