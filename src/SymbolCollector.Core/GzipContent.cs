using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace SymbolCollector.Core
{
    internal class GzipContent : HttpContent
    {
        private const string Gzip = "gzip";
        private readonly HttpContent _content;

        public GzipContent(HttpContent content)
        {
            Debug.Assert(content != null);
            _content = content;

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

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            var gzipStream = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
            try
            {
                await _content.CopyToAsync(gzipStream).ConfigureAwait(false);
            }
            finally
            {
                gzipStream.Dispose();
            }
        }
    }
}
