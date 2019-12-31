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
        Android
    }

    public interface ISymbolClient : IDisposable
    {
        Task<Guid> Start(string friendlyName, BatchType batchType, CancellationToken token);
        Task<Guid> Close(Guid batchId, IClientMetrics? metrics, CancellationToken token);

        Task<bool> Upload(
            Guid batchId,
            string buildId,
            string? hash,
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
            _httpClient = new HttpClient(handler ?? new HttpClientHandler());
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                $"{assemblyName?.Name ?? "SymbolCollector"}/{assemblyName?.Version?.ToString() ?? "0.0.0"}");

            _baseAddress = baseAddress;
            assemblyName ??= Assembly.GetEntryAssembly()?.GetName();

            _logger = logger;
        }

        public async Task<Guid> Start(string friendlyName, BatchType batchType, CancellationToken token)
        {
            var batchId = Guid.NewGuid();
            var body = new {BatchFriendlyName = friendlyName, BatchType = batchType};

            HttpContent content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            var response = await _httpClient.PostAsync($"{_baseAddress.AbsoluteUri}/symbol/batch/{batchId}/start",
                content, token);
            await ThrowForUnsuccessful("Could not start batch.", response);
            return batchId;
        }

        public async Task<Guid> Close(Guid batchId, IClientMetrics? metrics, CancellationToken token)
        {
            var body = new {ClientMetrics = metrics};

            var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            var response = await _httpClient.PostAsync(
                $"{_baseAddress.AbsoluteUri}/symbol/batch/{batchId}/close",
                content,
                token);

            await ThrowForUnsuccessful("Could not close batch.", response);
            return batchId;
        }

        public async Task<bool> Upload(
            Guid batchId,
            string buildId,
            string? hash,
            string fileName,
            Stream file,
            CancellationToken token)
        {
            {
                var checkUrl = $"{_baseAddress.AbsoluteUri}/symbol/batch/{batchId}/check/{buildId}";
                if (string.IsNullOrWhiteSpace(hash))
                {
                    checkUrl = $"{checkUrl}/{hash}";
                }

                var checkResponse =
                    await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, checkUrl), token);

                if (checkResponse.StatusCode == HttpStatusCode.Conflict)
                {
                    _logger.LogDebug("Server returns {statusCode} for {buildId}",
                        checkResponse.StatusCode, buildId);
                    return true; // already in the server, consider it successful.
                }

                await ThrowForUnsuccessful("Failed checking if file is needed.", checkResponse);
            }
            {
                var uploadUrl = $"{_baseAddress.AbsoluteUri}/symbol/batch/{batchId}/upload";
                var uploadResponse = await _httpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, uploadUrl)
                    {
                        Content = new MultipartFormDataContent {{new StreamContent(file), fileName, fileName}}
                    }, token);

                await ThrowForUnsuccessful("Failed uploading file.", uploadResponse);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var responseBody = await uploadResponse.Content.ReadAsStringAsync();
                    _logger.LogDebug("Upload response body: {body}", responseBody);
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
