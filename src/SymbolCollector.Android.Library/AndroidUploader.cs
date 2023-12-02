using Microsoft.Extensions.Logging;
using Sentry;
using SymbolCollector.Core;
using Exception = System.Exception;

namespace SymbolCollector.Android.Library;

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
            var paths = new[] { "/system/lib", "/system/lib64", "/system/", "/vendor/lib" };

            _logger.LogInformation("Using friendly name: {friendlyName} and paths: {paths}",
                friendlyName, paths);

            try
            {
                await _client.UploadAllPathsAsync(friendlyName, BatchType.Android, paths, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed uploading {friendlyName} paths: {paths}",
                    friendlyName, paths);
                // Make sure event is flushed and rethrow
                await SentrySdk.FlushAsync(TimeSpan.FromSeconds(3));
                throw;
            }
        }, token);
}