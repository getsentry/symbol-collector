using Microsoft.Extensions.Logging;
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

    public Task StartUpload(string friendlyName, ISpan uploadSpan, CancellationToken token) =>
        Task.Run(async () =>
        {
            var paths = new[] { "/system/lib", "/system/lib64", "/system/", "/vendor/lib" };

            _logger.LogInformation("Using friendly name: {friendlyName} and paths: {paths}",
                friendlyName, paths);

            var androidUploaderSpan = uploadSpan.StartChild("androidUpload", "uploading all paths async");
            try
            {
                await _client.UploadAllPathsAsync(friendlyName, BatchType.Android, paths, androidUploaderSpan, token);
                androidUploaderSpan.Finish();
            }
            catch (OperationCanceledException)
            {
                androidUploaderSpan.Finish(SpanStatus.Cancelled);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed uploading {friendlyName} paths: {paths}",
                    friendlyName, paths);
                androidUploaderSpan.Finish(e);
                // Make sure event is flushed and rethrow
                await SentrySdk.FlushAsync(TimeSpan.FromSeconds(3));
                throw;
            }
        }, token);
}
