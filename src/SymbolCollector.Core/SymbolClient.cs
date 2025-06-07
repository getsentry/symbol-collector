using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SymbolCollector.Core;

public class SymbolClientOptions
{
    public Uri BaseAddress { get; set; } = null!;

    public bool Http2 { get; set; } = false;

    // Big batches take ages to close
    public TimeSpan HttpClientTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public string UserAgent { get; set; } = "SymbolCollector/0.0.0";
    public int ParallelTasks { get; set; } = 10;
    public HashSet<string> BlockListedPaths { get; set; } = new();
}

// prefix to final structure: ios, watchos, macos, android
public enum BatchType
{
    Unknown,

    // watchos
    WatchOS,

    // macos
    MacOS,

    // ios
    IOS,

    // android (doesn't exist yet)
    Android,

    // linux (doesn't exist yet)
    // TODO: break up in distributions
    Linux
}

public static class BatchTypeExtensions
{
    public static string ToSymsorterPrefix(this BatchType type) =>
        type switch
        {
            BatchType.WatchOS => "watchos",
            BatchType.MacOS => "macos",
            BatchType.IOS => "ios",
            BatchType.Android => "android",
            BatchType.Linux => "linux",
            _ => throw new InvalidOperationException($"Invalid BatchType {type}."),
        };
}

public interface ISymbolClient : IDisposable
{
    Task<Guid> Start(string friendlyName, BatchType batchType, CancellationToken token);
    Task<Guid> Close(Guid batchId, CancellationToken token);

    Task<bool> Upload(
        Guid batchId,
        string unifiedId,
        string hash,
        string fileName,
        Func<Stream> fileFactory,
        CancellationToken token);
}

public class SymbolClient : ISymbolClient
{
    private readonly SymbolClientOptions _options;
    private readonly ClientMetrics _metrics;
    private readonly ILogger<SymbolClient> _logger;
    private readonly IHub _hub;
    private readonly HttpClient _httpClient;
    private readonly Version _httpVersion;

    public SymbolClient(
        IHub hub,
        SymbolClientOptions options,
        ClientMetrics metrics,
        ILogger<SymbolClient> logger,
        HttpClient httpClient)
    {
        _hub = hub;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient), "httpClient is required.");

        httpClient.DefaultRequestHeaders.Add("User-Agent", options.UserAgent);
        httpClient.Timeout = options.HttpClientTimeout;

        _options = options;
        _metrics = metrics;
        _logger = logger;
        _httpVersion = Version.Parse(_options.Http2 ? "2.0" : "1.1");
    }

    public async Task<Guid> Start(string friendlyName, BatchType batchType, CancellationToken token)
    {
        var batchId = Guid.NewGuid();
        _hub.ConfigureScope(s => s.SetTag("batchId", batchId.ToString()));

        var body = new {BatchFriendlyName = friendlyName, BatchType = batchType};

        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

        var url = $"{_options.BaseAddress.AbsoluteUri}symbol/batch/{batchId}/start";
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url) {Version = _httpVersion, Content = content};
            var response = await _httpClient.SendAsync(request, token);
            await ThrowOnUnsuccessfulResponse("Could not start batch.", response);
        }
        catch (Exception e)
        {
            e.Data["URL"] = url;
            throw;
        }

        return batchId;
    }

    public async Task<Guid> Close(Guid batchId, CancellationToken token)
    {
        var body = new {ClientMetrics = _metrics};

        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

        var url = $"{_options.BaseAddress.AbsoluteUri}symbol/batch/{batchId}/close";
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url) {Version = _httpVersion, Content = content};
            var response = await _httpClient.SendAsync(request, token);
            await ThrowOnUnsuccessfulResponse("Could not close batch.", response);
        }
        catch (Exception e)
        {
            using var _ = _logger.BeginScope(("url", url));
            _logger.LogError(e, "Failed to close batch through {url}", url);
            throw;
        }

        return batchId;
    }

    public async Task<bool> Upload(
        Guid batchId,
        string unifiedId,
        string hash,
        string fileName,
        Func<Stream> fileFactory,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(unifiedId))
        {
            throw new ArgumentException("Invalid empty BuildId");
        }

        return await IsSymbolMissing() && await Upload();

        async Task<bool> IsSymbolMissing()
        {
            var checkUrl = $"{_options.BaseAddress.AbsoluteUri}symbol/batch/{batchId}/check/v2/{unifiedId}/{hash}";
            try
            {
                var checkResponse =
                    await _httpClient.SendAsync(
                        new HttpRequestMessage(HttpMethod.Head, checkUrl) {Version = _httpVersion}, token);

                if (checkResponse.StatusCode == HttpStatusCode.Conflict
                    || checkResponse.StatusCode == HttpStatusCode.AlreadyReported)
                {
                    _logger.LogDebug("Server returns {statusCode} for {buildId}",
                        checkResponse.StatusCode, unifiedId);
                    return false;
                }

                await ThrowOnUnsuccessfulResponse("Failed checking if file is needed.", checkResponse);
            }
            catch (Exception e)
            {
                e.Data["url"] = checkUrl;
                _logger.LogWarning(e, "Failed to check for unifiedId through {url}", checkUrl);
                throw;
            }

            return true;
        }

        async Task<bool> Upload()
        {
            var uploadUrl = $"{_options.BaseAddress.AbsoluteUri}symbol/batch/{batchId}/upload";
            HttpResponseMessage? uploadResponse = null;
            Stream? fileStream = null;
            try
            {
                fileStream = fileFactory();
                var fileContentStream = new StreamContent(fileStream);
                fileContentStream.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");

                var content = new MultipartFormDataContent
                {
                    { new GzipContent(fileContentStream, _metrics), fileName, fileName }
                };
                uploadResponse = await _httpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, uploadUrl)
                    {
                        Version = _httpVersion,
                        Content = content
                    }, token);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var responseBody = await uploadResponse.Content.ReadAsStringAsync();
                    _logger.LogDebug("Upload response body: {body}", responseBody);
                }

                if (uploadResponse.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    _logger.LogDebug("Server returns {statusCode} for {buildId}",
                        uploadResponse.StatusCode, unifiedId);
                    return false;
                }

                await ThrowOnUnsuccessfulResponse("Failed uploading file.", uploadResponse);

                _logger.LogInformation("File {file} with {bytes} was uploaded successfully.",
                    fileName, fileStream.Length);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to upload file");
                // We can capture these if we decide to debug each file
                // SentrySdk.CaptureException(e, s =>
                // {
                //     s.AddAttachment(fileFactory(), fileName);
                //     s.SetExtra("url", uploadUrl);
                // });
                throw;
            }
            finally
            {
                uploadResponse?.Dispose();
                if (fileStream is not null)
                {
                    await fileStream.DisposeAsync();
                }
            }

            return true;
        }
    }

    private static async Task ThrowOnUnsuccessfulResponse(string message, HttpResponseMessage checkResponse)
    {
        if (!checkResponse.IsSuccessStatusCode)
        {
            var messageFormat = $"{message} Server response: {checkResponse.StatusCode}";
            var ex = new InvalidOperationException(messageFormat);

            var responseBody = await checkResponse.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                ex.Data[nameof(responseBody)] = responseBody;
            }

            throw ex;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
