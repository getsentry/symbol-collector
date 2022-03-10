using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sentry;

namespace SymbolCollector.Core
{
    public class SymbolClientOptions
    {
        public Uri BaseAddress { get; set; } = null!;

        public bool Http2 { get; set; } = false;

        // Big batches take ages to close
        public TimeSpan HttpClientTimeout { get; set; } = TimeSpan.FromMinutes(2);
        public string UserAgent { get; set; } = "SymbolCollector/0.0.0";
        public int ParallelTasks { get; set; } = 10;
        public HashSet<string> BlackListedPaths { get; set; } = new HashSet<string>();
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
        Task<Guid> Close(Guid batchId, IClientMetrics? metrics, CancellationToken token);

        Task<bool> Upload(
            Guid batchId,
            string unifiedId,
            string hash,
            string fileName,
            Stream file,
            CancellationToken token);
    }

    public class SymbolClient : ISymbolClient
    {
        private readonly SymbolClientOptions _options;
        private readonly ILogger<SymbolClient> _logger;
        private readonly IHub _hub;
        private readonly HttpClient _httpClient;
        private readonly Version _httpVersion;

        public SymbolClient(
            IHub hub,
            SymbolClientOptions options,
            ILogger<SymbolClient> logger,
            HttpClient httpClient)
        {
            _hub = hub;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient), "httpClient is required.");

            httpClient.DefaultRequestHeaders.Add("User-Agent", options.UserAgent);
            httpClient.Timeout = options.HttpClientTimeout;

            _options = options;
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
                await ThrowForUnsuccessful("Could not start batch.", response);
            }
            catch (Exception e)
            {
                e.Data["URL"] = url;
                throw;
            }

            return batchId;
        }

        public async Task<Guid> Close(Guid batchId, IClientMetrics? metrics, CancellationToken token)
        {
            var body = new {ClientMetrics = metrics};

            var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            var url = $"{_options.BaseAddress.AbsoluteUri}symbol/batch/{batchId}/close";
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url) {Version = _httpVersion, Content = content};
                var response = await _httpClient.SendAsync(request, token);
                await ThrowForUnsuccessful("Could not close batch.", response);
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
            Stream file,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(unifiedId))
            {
                throw new ArgumentException("Invalid empty BuildId");
            }

            return await IsSymbolMissing(batchId, unifiedId, hash, token)
                   && await Upload(batchId, unifiedId, fileName, file, token);
        }

        async Task<bool> IsSymbolMissing(
                Guid batchId,
                string unifiedId,
                string hash,
                CancellationToken token)
        {
            var checkUrl = $"{_options.BaseAddress.AbsoluteUri}symbol/batch/{batchId}/check/v2/{unifiedId}/{hash}";
            try
            {
                return await Retry(async () =>
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

                    await ThrowForUnsuccessful("Failed checking if file is needed.", checkResponse);
                    return true;
                }, token);
            }
            catch (Exception e)
            {
                using var _ = _logger.BeginScope(("url", checkUrl));
                _logger.LogError(e, "Failed to check for unifiedId through {url}", checkUrl);
                throw;
            }
        }

        private async Task<bool> Upload(
            Guid batchId,
            string unifiedId,
            string fileName,
            Stream file,
            CancellationToken token)
        {
            var uploadUrl = $"{_options.BaseAddress.AbsoluteUri}symbol/batch/{batchId}/upload";
            try
            {
                var result = await Retry(async () =>
                {
                    var uploadResponse = await _httpClient.SendAsync(
                        new HttpRequestMessage(HttpMethod.Post, uploadUrl)
                        {
                            Version = _httpVersion,
                            Content = new MultipartFormDataContent
                            {
                                { new GzipContent(new StreamContent(file)), fileName, fileName }
                            }
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

                    await ThrowForUnsuccessful("Failed uploading file.", uploadResponse);
                    return true;
                }, token);

                _logger.LogInformation("File {file} with {bytes} upload was {result}.",
                    fileName, file.Length, result ? "successful" : "unsuccessful");

                return result;
            }
            catch (Exception e)
            {
                using var _ = _logger.BeginScope(("url", uploadUrl));
                _logger.LogError(e, "Failed to upload through {url}", uploadUrl);
                throw;
            }
        }

        private static async Task<bool> Retry(Func<Task<bool>> action, CancellationToken token)
        {
            for (var i = 0; i < 4; i++)
            {
                try
                {
                    return await action();
                }
                // Single retry as an attempt to reduce Android error:
                // Read error: ssl=0x7ac5984c08: SSL_ERROR_WANT_READ occurred. You should never see this.
                catch (WebException e) when (e.Message.Contains("You should never see this."))
                {
                    if (i > 2)
                    {
                        throw;
                    }

                    await Task.Delay(100, token);
                }
            }

            return false;
        }
        private static async Task ThrowForUnsuccessful(string message, HttpResponseMessage checkResponse)
        {
            if (!checkResponse.IsSuccessStatusCode)
            {
                var messageFormat = $"{message} Server response: {checkResponse.StatusCode}";
                var responseBody = await checkResponse.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(responseBody))
                {
                    messageFormat = $"{message}\n{responseBody}";
                }

                var ex = new InvalidOperationException(messageFormat);
                const string traceIdKey = "TraceIdentifier";
                if (checkResponse.Headers.TryGetValues(traceIdKey, out var traceIds))
                {
                    ex.Data[traceIdKey] = traceIds.FirstOrDefault() ?? "unknown";
                }

                throw ex;
            }
        }

        public void Dispose() => _httpClient.Dispose();
    }
}
