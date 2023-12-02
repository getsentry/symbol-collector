using System.Diagnostics;
using System.IO.Compression;
using System.Net;

namespace SymbolCollector.Core
{
    internal class GzipContent : HttpContent
    {
        private const string Gzip = "gzip";
        private readonly HttpContent _content;
        private readonly ClientMetrics _metrics;

        public GzipContent(HttpContent content, ClientMetrics metrics)
        {
            Debug.Assert(content != null);
            _content = content;
            _metrics = metrics;

            foreach (var header in content.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            Headers.ContentEncoding.Add(Gzip);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var countingGzipStream = new CounterStream(
                new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true),
                _metrics);
            try
            {
                await _content.CopyToAsync(countingGzipStream).ConfigureAwait(false);
            }
            finally
            {
                await countingGzipStream.DisposeAsync();
            }
        }
    }
}
