using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace SymbolCollector.Server.Services
{
    public class SymbolService : SymbolCollection.SymbolCollectionBase
    {
        private readonly ILogger<SymbolService> _logger;

        public SymbolService(ILogger<SymbolService> logger) => _logger = logger;

        public override async Task<Empty> Uploads(IAsyncStreamReader<SymbolUploadRequest> requestStream, ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync())
            {
                _logger.LogInformation("Received symbol with id {debugId} file size {size}",  message.DebugId, message.File.Length);
                // TODO:
            }

            return new Empty();
        }
    }
}
