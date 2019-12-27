using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Upload;
using Google.Cloud.Storage.V1;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SymbolCollector.Core;

namespace SymbolCollector.Server.Tests
{
    public class IntegrationTests : IClassFixture<WebApplicationFactory<Startup>>
    {
        private readonly Fixture _fixture;

        public IntegrationTests(WebApplicationFactory<Startup> factory)
        {
            _fixture = new Fixture(factory);
        }

        private class Fixture
        {
            private readonly Action<IServiceCollection> _defaultMocks;
            private readonly WebApplicationFactory<Startup> _factory;
            public Action<IServiceCollection>? ConfigureServices { get; set; }
            public StorageClient StorageClient { get; set; } = Substitute.For<StorageClient>();
            public IStorageClientFactory StorageClientFactory { get; set; } = Substitute.For<IStorageClientFactory>();

            public Fixture(WebApplicationFactory<Startup> factory)
            {
                _factory = factory;
                StorageClientFactory.Create().Returns(Task.FromResult(StorageClient));
                _defaultMocks = c => c.AddSingleton(StorageClientFactory);
            }

            public HttpClient GetClient() =>
                _factory.WithWebHostBuilder(c =>
                        c.ConfigureServices(s =>
                        {
                            _defaultMocks(s);
                            ConfigureServices?.Invoke(s);
                        }))
                    .CreateClient();
        }

        [Fact(Skip = "not writing to GCS atm")]
        public async Task Put_StoresFileInGcs()
        {
            var client = _fixture.GetClient();
            const string expectedFileName = "libfake.so";

            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route)
            {
                Content = new MultipartFormDataContent
                {
                    {new StringContent("fake stuff"), expectedFileName, expectedFileName}
                }
            });

            Assert.True(resp.IsSuccessStatusCode);
            await _fixture.StorageClient.Received(1).UploadObjectAsync(
                Arg.Any<string>(),
                expectedFileName,
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<UploadObjectOptions>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<IProgress<IUploadProgress>>());
        }

        [Fact]
        public async Task Start_MissingBatchFriendlyName_BadRequest()
        {
            var model = new BatchStartRequestModel();

            var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(model));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            var client = _fixture.GetClient();
            var resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + "/batch/start") {Content = content});

            var responseStream = await resp.Content.ReadAsStreamAsync();
            var responseModel = await JsonSerializer.DeserializeAsync<DynamicAttribute>(responseStream);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            // Assert.Equal("Missing friendly name", responseModel.message);
        }

        [Fact]
        public async Task Start_TooLongFriendlyName_BadRequest()
        {
        }

        [Fact]
        public async Task Start_BatchTypeUnknown_BadRequest()
        {
            Assert.False(true);
        }

        [Fact]
        public void UploadSymbol_BatchNotStarted_BadRequest()
        {
            Assert.False(true);
        }

        [Fact]
        public void UploadSymbol_AlreadyExists_SymbolRelatesToBatch()
        {
            Assert.False(true);
        }

        [Fact]
        public void UploadSymbol_DoesNotExists_SymbolAdded()
        {
            Assert.False(true);
        }

        [Fact]
        public void UploadSymbol_HashConflict_ReturnsConflict()
        {
            Assert.False(true);
        }

        [Fact]
        public async Task Batch_StartToEnd()
        {
            var registration = new BatchStartRequestModel
            {
                BatchFriendlyName = "Test batch", BatchType = BatchType.Android
            };

            HttpContent content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(registration));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            var batchId = Guid.NewGuid();
            var client = _fixture.GetClient();
            var resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/start")
                {
                    Content = content
                });

            // var responseStream = await resp.Content.ReadAsStreamAsync();
            // var responseModel = await JsonSerializer.DeserializeAsync<BatchStartResponseModel>(responseStream);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            // Assert.NotEqual(default, responseModel.BatchId);


            var debugId = "df3a9df5-26a8-d63d-88ad-820f74a325b5";
            // var hash = ""; // TODO Send hash
            resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
                SymbolsController.Route + $"/batch/{batchId}/check/{debugId}"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);


            // TODO: Move this to repo/test/
            var testFile = "../../../../SymbolCollector.Core.Tests/TestFiles/libxamarin-app.so";
            Assert.True(File.Exists(testFile));

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/upload/")
                {
                    Content = new MultipartFormDataContent
                    {
                        {
                            new ByteArrayContent(File.ReadAllBytes(testFile)), testFile, Path.GetFileName(testFile)
                        }
                    }
                });

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);


            var responseStream = await resp.Content.ReadAsStreamAsync();
            var responseModel = await JsonSerializer.DeserializeAsync<JsonElement>(responseStream);
            Assert.Equal(1, responseModel.GetProperty("filesCreated").GetInt32());

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/upload/")
                {
                    Content = new MultipartFormDataContent
                    {
                        {
                            new ByteArrayContent(File.ReadAllBytes(testFile)), testFile, Path.GetFileName(testFile)
                        }
                    }
                });

            responseStream = await resp.Content.ReadAsStreamAsync();
            responseModel = await JsonSerializer.DeserializeAsync<JsonElement>(responseStream);
            Assert.Equal(0, responseModel.GetProperty("filesCreated").GetInt32());
            Assert.Equal(HttpStatusCode.AlreadyReported, resp.StatusCode);

            var metrics = new ClientMetricsModel
            {
                StartedTime = DateTimeOffset.UtcNow.AddSeconds(-1),
                SuccessfullyUploadCount = 1,
                FailedToUploadCount = 1,
                FilesProcessedCount = 2,
                BatchesProcessedCount = 1
            };

            var model = new BatchEndRequestModel {ClientMetrics = metrics};

            content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(model));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/close/")
                {
                    Content = content
                });

            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        }
    }
}
