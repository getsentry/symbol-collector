using System;
using Microsoft.Extensions.Http;
using Sentry;

namespace SymbolCollector.Android.Library
{
    // can be deleted once https://github.com/getsentry/sentry-dotnet/issues/824 is fixed
    internal class SentryHttpMessageHandlerBuilderFilter : IHttpMessageHandlerBuilderFilter
    {
        private readonly Func<IHub> _getHub;

        public SentryHttpMessageHandlerBuilderFilter(Func<IHub> getHub) =>
            _getHub = getHub;

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
            handlerBuilder =>
            {
                var hub = _getHub();
                handlerBuilder.AdditionalHandlers.Add(new SentryHttpMessageHandler(hub));
                next(handlerBuilder);
            };
    }
}
