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

namespace SymbolCollector.Core
{
    public class SymbolClientOptions
    {
        public Uri BaseAddress { get; set; } = null!;

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
        private readonly HttpClient _httpClient;

        public SymbolClient(
            SymbolClientOptions options,
            ILogger<SymbolClient> logger,
            HttpMessageHandler? handler = null)
        {
            _httpClient = new HttpClient(handler ?? new HttpClientHandler()) {Timeout = options.HttpClientTimeout};
            _httpClient.DefaultRequestHeaders.Add("User-Agent", options.UserAgent);
            _options = options;
            _logger = logger;
        }

        public async Task<Guid> Start(string friendlyName, BatchType batchType, CancellationToken token)
        {
            var batchId = Guid.NewGuid();
            var body = new {BatchFriendlyName = friendlyName, BatchType = batchType};

            HttpContent content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            var url = $"{_options.BaseAddress.AbsoluteUri}symbol/batch/{batchId}/start";
            try
            {
                var response = await _httpClient.PostAsync(url,
                    content, token);
                await ThrowForUnsuccessful("Could not start batch.", response);
            }
            catch (Exception e)
            {
                using var _ = _logger.BeginScope(("url", url));
                _logger.LogError(e, "Failed to start batch through {url}", url);
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
                var response = await _httpClient.PostAsync(
                    url,
                    content,
                    token);
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

            {
                var checkUrl = $"{_options.BaseAddress.AbsoluteUri}symbol/batch/{batchId}/check/{unifiedId}/{hash}";
                try
                {
                    var checkResponse =
                        await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, checkUrl), token);

                    if (checkResponse.StatusCode == HttpStatusCode.Conflict)
                    {
                        _logger.LogDebug("Server returns {statusCode} for {buildId}",
                            checkResponse.StatusCode, unifiedId);
                        return false;
                    }

                    await ThrowForUnsuccessful("Failed checking if file is needed.", checkResponse);
                }
                catch (Exception e)
                {
                    using var _ = _logger.BeginScope(("url", checkUrl));
                    _logger.LogError(e, "Failed to check for unifiedId through {url}", checkUrl);
                    throw;
                }
            }
            {
                var uploadUrl = $"{_options.BaseAddress.AbsoluteUri}symbol/batch/{batchId}/upload";
                try
                {
                    var uploadResponse = await _httpClient.SendAsync(
                        new HttpRequestMessage(HttpMethod.Post, uploadUrl)
                        {
                            Content = new MultipartFormDataContent {{new StreamContent(file), fileName, fileName}}
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
                }
                catch (Exception e)
                {
                    using var _ = _logger.BeginScope(("url", uploadUrl));
                    _logger.LogError(e, "Failed to upload through {url}", uploadUrl);
                    throw;
                }

                _logger.LogInformation("File {file} with {bytes} was uploaded successfully.",
                    fileName, file.Length);

                return true;
            }
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
