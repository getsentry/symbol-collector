using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SymbolCollector.Core
{
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
            string buildId,
            string hash,
            string fileName,
            Stream file,
            CancellationToken token);
    }

    public class SymbolClient : ISymbolClient
    {
        private readonly Uri _baseAddress;
        private readonly ILogger<SymbolClient> _logger;
        private readonly HttpClient _httpClient;

        public SymbolClient(
            Uri baseAddress,
            ILogger<SymbolClient> logger,
            HttpMessageHandler? handler = null,
            AssemblyName? assemblyName = null)
        {
            _httpClient = new HttpClient(handler ?? new HttpClientHandler())
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            assemblyName ??= Assembly.GetEntryAssembly()?.GetName();
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                $"{assemblyName?.Name ?? "SymbolCollector"}/{assemblyName?.Version?.ToString() ?? "0.0.0"}");

            _baseAddress = baseAddress;

            _logger = logger;
        }

        public async Task<Guid> Start(string friendlyName, BatchType batchType, CancellationToken token)
        {
            var batchId = Guid.NewGuid();
            var body = new {BatchFriendlyName = friendlyName, BatchType = batchType};

            HttpContent content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            var url = $"{_baseAddress.AbsoluteUri}symbol/batch/{batchId}/start";
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

            var url = $"{_baseAddress.AbsoluteUri}symbol/batch/{batchId}/close";
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
            string buildId,
            string hash,
            string fileName,
            Stream file,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(buildId))
            {
                throw new ArgumentException("Invalid empty BuildId");
            }
            {
                var checkUrl = $"{_baseAddress.AbsoluteUri}symbol/batch/{batchId}/check/{buildId}/{hash}";
                try
                {
                    var checkResponse =
                        await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, checkUrl), token);

                    if (checkResponse.StatusCode == HttpStatusCode.Conflict)
                    {
                        _logger.LogDebug("Server returns {statusCode} for {buildId}",
                            checkResponse.StatusCode, buildId);
                        return false;
                    }

                    await ThrowForUnsuccessful("Failed checking if file is needed.", checkResponse);
                }
                catch (Exception e)
                {
                    using var _ = _logger.BeginScope(("url", checkUrl));
                    _logger.LogError(e, "Failed to check for debugid through {url}", checkUrl);
                    throw;
                }
            }
            {
                var uploadUrl = $"{_baseAddress.AbsoluteUri}symbol/batch/{batchId}/upload";
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

                throw new InvalidOperationException(messageFormat);
            }
        }

        public void Dispose() => _httpClient.Dispose();
    }
}
