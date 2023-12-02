using System;
using System.Threading;
using System.Threading.Tasks;
using Sentry;

namespace SymbolCollector.Core
{
    public static class SpanExtensions
    {
        public static void TrackSpan(
            this ISpan parentSpan,
            Action callback,
            string operation,
            string? description = null)
        {
            // ! can be removed once https://github.com/getsentry/sentry-dotnet/issues/825 is addressed.
            var span = parentSpan.StartChild(operation, description!);
            try
            {
                callback();
                span.Finish();
            }
            catch (Exception e)
            {
                span.Finish(e);
                throw;
            }
        }

        public static void Finish(this ISpan span, Exception e)
        {
            var status = e switch
            {
                ThreadAbortException _ => SpanStatus.Aborted,
                TaskCanceledException _ => SpanStatus.Cancelled,
                OperationCanceledException _ => SpanStatus.Cancelled,
                NotImplementedException _ => SpanStatus.Unimplemented,
                ArgumentOutOfRangeException _ => SpanStatus.OutOfRange,
                IndexOutOfRangeException _ => SpanStatus.OutOfRange,
                _ => SpanStatus.InternalError
                // _ => SpanStatus.UnknownError
            };

            // TODO: Weak ref to Exception so that CaptureException later can find the span
            span.Finish(status);
        }
    }
}
