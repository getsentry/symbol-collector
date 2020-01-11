using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SymbolCollector.Core;
using Exception = System.Exception;

namespace SymbolCollector.Android
{
    public class AndroidUploader
    {
        private readonly Client _client;
        private readonly ILogger<AndroidUploader> _logger;

        public AndroidUploader(Client client, ILogger<AndroidUploader> logger)
        {
            _client = client;
            _logger = logger;
        }

        public Task StartUpload(string friendlyName, CancellationToken token) =>
            Task.Run(async () =>
            {
                var paths = new[] {"/system/lib", "/system/lib64", "/system/", "/vendor/lib"};

                _logger.LogInformation("Using friendly name: {friendlyName} and paths: {paths}",
                    friendlyName, paths);

                try
                {
                    await _client.UploadAllPathsAsync(friendlyName, BatchType.Android, paths, token);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed uploading {friendlyName} paths: {paths}",
                        friendlyName, paths);
                }
            }, token);
    }
}
