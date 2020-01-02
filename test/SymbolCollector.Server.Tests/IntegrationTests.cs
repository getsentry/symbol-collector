using System;
using System.IO;
using System.Linq;
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

            public IServiceProvider? ServiceProvider { get; set; }

            public Fixture(WebApplicationFactory<Startup> factory)
            {
                _factory = factory;
                StorageClientFactory.Create().Returns(Task.FromResult(StorageClient));
                _defaultMocks = c => c.AddSingleton(StorageClientFactory);
            }

            public HttpClient GetClient()
            {
                _factory.WithWebHostBuilder(c =>
                    c.ConfigureServices(s =>
                    {
                        _defaultMocks(s);
                        ConfigureServices?.Invoke(s);
                    }));
                ServiceProvider = _factory.Services;
                return _factory.CreateClient();
            }
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
        public async Task Start_MissingBody_BadRequest()
        {
            var client = _fixture.GetClient();
            var resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{Guid.NewGuid()}/start")
                {
                    Content = new JsonContent(new { })
                });

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var responseModel = await resp.Content.ToJsonElement();
            Assert.Equal("The field BatchType must be between 1 and 4.",
                responseModel.GetProperty("BatchType")[0].GetString());
            Assert.Equal("The Batch friendly name field is required.",
                responseModel.GetProperty("BatchFriendlyName")[0].GetString());
        }

        [Theory]
        [InlineData(BatchType.Unknown, true)]
        [InlineData(BatchType.Android, false)]
        [InlineData(BatchType.IOS, false)]
        [InlineData(BatchType.MacOS, false)]
        [InlineData(BatchType.WatchOS, false)]
        [InlineData((BatchType)(((int)BatchType.Android) + 1), true)]
        public async Task Start_BatchType_TestCase(BatchType batchType, bool validationError)
        {
            var model = new BatchStartRequestModel {BatchType = batchType};

            var client = _fixture.GetClient();
            var resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{Guid.NewGuid()}/start")
                {
                    Content = new JsonContent(model)
                });

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var responseModel = await resp.Content.ToJsonElement();
            if (validationError)
            {
                Assert.Equal("The field BatchType must be between 1 and 4.",
                    responseModel.GetProperty("BatchType")[0].GetString());
            }
            else
            {
                Assert.False(responseModel.TryGetProperty("BatchType", out _));
            }
        }

        [Fact]
        public async Task Start_TooLongFriendlyName_BadRequest()
        {
            var model = new BatchStartRequestModel
            {
                BatchFriendlyName = new string('x', 1001), BatchType = BatchType.Android
            };

            var client = _fixture.GetClient();
            var resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{Guid.NewGuid()}/start")
                {
                    Content = new JsonContent(model)
                });

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var responseModel = await resp.Content.ToJsonElement();
            Assert.Equal("A batch friendly name can't be longer than 1000 characters.",
                responseModel.GetProperty("BatchFriendlyName")[0].GetString());
        }

        [Fact]
        public async Task UploadSymbol_BatchNotStarted_BadRequest()
        {
            var client = _fixture.GetClient();
            var batchId = Guid.NewGuid();
            var resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/upload/")
                {
                    Content = new MultipartFormDataContent()
                });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var responseModel = await resp.Content.ToJsonElement();
            Assert.Equal($"Batch Id {batchId} does not exist.", responseModel.GetProperty("Batch")[0].GetString());
        }

        [Fact]
        public async Task UploadSymbol_AlreadyExists_SymbolRelatesToBatch()
        {
            var client = _fixture.GetClient();
            var batchId1 = Guid.NewGuid();
            var batchId2 = Guid.NewGuid();
            var resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId1}/start")
                {
                    Content = new JsonContent(new BatchStartRequestModel
                    {
                        BatchFriendlyName = "Test batch 1", BatchType = BatchType.Android
                    })
                });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId2}/start")
                {
                    Content = new JsonContent(new BatchStartRequestModel
                    {
                        BatchFriendlyName = "Test batch 1", BatchType = BatchType.Android
                    })
                });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var testFile = Path.Combine("TestFiles", "libqcbassboost.so");
            const string debugId = "637aa379-d34e-d455-c314-d646b8f3eaec";
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId1}/upload/")
                {
                    Content = new MultipartFormDataContent
                    {
                        {
                            new ByteArrayContent(File.ReadAllBytes(testFile)), testFile, Path.GetFileName(testFile)
                        }
                    }
                });
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId2}/upload/")
                {
                    Content = new MultipartFormDataContent
                    {
                        {
                            new ByteArrayContent(File.ReadAllBytes(testFile)), testFile, Path.GetFileName(testFile)
                        }
                    }
                });
            Assert.Equal(HttpStatusCode.AlreadyReported, resp.StatusCode);

            var symbolService = _fixture.ServiceProvider.GetRequiredService<ISymbolService>();
            var symbol = await symbolService.GetSymbol(debugId, CancellationToken.None);

            var batch1 = await symbolService.GetBatch(batchId1, CancellationToken.None);
            var storedSymbol = Assert.Single(batch1!.Symbols).Value!;
            Assert.Equal(symbol!.DebugId, storedSymbol.DebugId);
            Assert.Equal(symbol!.Hash, storedSymbol.Hash);

            var batch2 = await symbolService.GetBatch(batchId1, CancellationToken.None);
            storedSymbol = Assert.Single(batch2!.Symbols).Value;
            Assert.Equal(symbol!.DebugId, storedSymbol.DebugId);
            Assert.Equal(symbol!.Hash, storedSymbol.Hash);
        }

        [Fact]
        public async Task UploadSymbol_DoesNotExists_SymbolAdded()
        {
            var registration = new BatchStartRequestModel
            {
                BatchFriendlyName = "Test batch", BatchType = BatchType.Android
            };

            var batchId = Guid.NewGuid();
            var client = _fixture.GetClient();
            var resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/start")
                {
                    Content = new JsonContent(registration)
                });

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var symbolService = _fixture.ServiceProvider.GetRequiredService<ISymbolService>();
            var batch = await symbolService.GetBatch(batchId, CancellationToken.None);
            Assert.Equal(batchId, batch!.BatchId);
            Assert.Empty(batch.Symbols);
            Assert.Equal(registration.BatchType, batch!.BatchType);
            Assert.False(batch.IsClosed);
            Assert.Equal(registration.BatchFriendlyName, batch!.FriendlyName);

            var testFile = Path.Combine("TestFiles", "libxamarin-app-arm64-v8a.so");
            const string debugId = "09752176-f337-f80b-e756-cec46b960391";

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

            var symbol = await symbolService.GetSymbol(debugId, CancellationToken.None);
            Assert.Equal(Path.GetFileName(testFile), symbol!.Name);
            Assert.Equal("5fb23797a8cb482bac325eabdcb3d7e70b89fe0ec51035010e9be3a7b76fff84", symbol.Hash);
            Assert.Equal(debugId, symbol.DebugId);
            Assert.EndsWith( Path.GetFileName(testFile), symbol.Path);
            Assert.StartsWith($"processing{Path.DirectorySeparatorChar}{batchId}{Path.DirectorySeparatorChar}", symbol.Path);
            Assert.Equal(batchId, symbol.BatchIds.Single());

            // TODO: Assert values once parsing is done.
            // Assert.Equal(FileFormat.Elf, symbol.FileFormat);
            // Assert.Equal(ObjectFileType.Executable, symbol.ObjectFileType);
            // Assert.Equal("x86", symbol.Arch);

            batch = await symbolService.GetBatch(batchId, CancellationToken.None);
            var storedSymbol = Assert.Single(batch!.Symbols).Value;
            Assert.Equal(symbol.DebugId, storedSymbol.DebugId);
            Assert.Equal(symbol.Hash, storedSymbol.Hash);
        }

        [Fact]
        public async Task Batch_StartToEnd()
        {
            var registration = new BatchStartRequestModel
            {
                BatchFriendlyName = "Test batch", BatchType = BatchType.Android
            };

            var batchId = Guid.NewGuid();
            var client = _fixture.GetClient();
            var resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/start")
                {
                    Content = new JsonContent(registration)
                });

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var testFile = Path.Combine("TestFiles", "libxamarin-app.so");
            const string debugId = "df3a9df5-26a8-d63d-88ad-820f74a325b5";
            const string hash = "1a40a2db7c6b4dd59e3bcecd9b53cf3c7fc544afc311e25c41ee01bc4bb99a96";
            resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
                SymbolsController.Route + $"/batch/{batchId}/check/{debugId}/{hash}"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

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

            var responseModel = await resp.Content.ToJsonElement();
            Assert.Equal(1, responseModel.GetProperty("filesCreated").GetInt32());

            // Check again if needed.
            resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
                SymbolsController.Route + $"/batch/{batchId}/check/{debugId}/{hash}"));
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);

            // Check again if with a different hash. API returns OK hoping the client uploads the file.
            resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
                SymbolsController.Route + $"/batch/{batchId}/check/{debugId}/{hash + "-wrong"}"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

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

            responseModel = await resp.Content.ToJsonElement();
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

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/close/")
                {
                    Content = new JsonContent(new BatchEndRequestModel {ClientMetrics = metrics})
                });

            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

            var symbolService = _fixture.ServiceProvider.GetRequiredService<ISymbolService>();
            var batch = await symbolService.GetBatch(batchId, CancellationToken.None);

            Assert.True(batch!.IsClosed);
            var symbol = Assert.Single(batch.Symbols).Value;
            File.Exists(symbol.Path);

            Assert.Equal(metrics.SuccessfullyUploadCount, batch!.ClientMetrics!.SuccessfullyUploadCount);
        }
    }
}
