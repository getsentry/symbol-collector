using System.Net;
using Google.Cloud.Storage.V1;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using SymbolCollector.Core;
using SymbolCollector.Server.Models;

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
            private WebApplicationFactory<Startup> _factory;
            public Action<IServiceCollection>? ConfigureServices { get; set; }
            public StorageClient StorageClient { get; set; } = Substitute.For<StorageClient>();
            public IStorageClientFactory StorageClientFactory { get; set; } = Substitute.For<IStorageClientFactory>();
            public IBatchFinalizer BatchFinalizer { get; set; } = Substitute.For<IBatchFinalizer>();

            public IServiceProvider? ServiceProvider { get; set; }

            public Fixture(WebApplicationFactory<Startup> factory)
            {
                _factory = factory;
                StorageClientFactory.Create().Returns(Task.FromResult(StorageClient));
                _defaultMocks = c =>
                {
                    c.AddSingleton(StorageClientFactory);
                    c.AddSingleton(BatchFinalizer);
                };
            }

            public HttpClient GetClient()
            {
                _factory = _factory.WithWebHostBuilder(c => c.ConfigureServices(s =>
                {
                    _defaultMocks(s);
                    ConfigureServices?.Invoke(s);
                }));
                ServiceProvider = _factory.Services;
                return _factory.CreateClient();
            }
        }

        [Fact]
        public async Task Health_Success()
        {
            var client = _fixture.GetClient();

            var resp = await client.GetAsync("/health");
            resp.AssertStatusCode(HttpStatusCode.OK);
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

            resp.AssertStatusCode(HttpStatusCode.BadRequest);
            var responseModel = await resp.Content.ToJsonElement();
            Assert.Equal("The field BatchType must be between 1 and 5.",
                responseModel.GetProperty("BatchType")[0].GetString());
            Assert.Equal("The Batch friendly name field is required.",
                responseModel.GetProperty("BatchFriendlyName")[0].GetString());
        }

        [Theory]
        [InlineData(BatchType.Unknown, true)]
        [InlineData(BatchType.Android, false)]
        [InlineData(BatchType.Linux, false)]
        [InlineData(BatchType.IOS, false)]
        [InlineData(BatchType.MacOS, false)]
        [InlineData(BatchType.WatchOS, false)]
        [InlineData((BatchType)((int)BatchType.Linux + 1), true)]
        public async Task Start_BatchType_TestCase(BatchType batchType, bool validationError)
        {
            var model = new BatchStartRequestModel {BatchType = batchType};

            var client = _fixture.GetClient();
            var resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{Guid.NewGuid()}/start")
                {
                    Content = new JsonContent(model)
                });

            resp.AssertStatusCode(HttpStatusCode.BadRequest);
            var responseModel = await resp.Content.ToJsonElement();
            if (validationError)
            {
                Assert.Equal("The field BatchType must be between 1 and 5.",
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

            resp.AssertStatusCode(HttpStatusCode.BadRequest);
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
            resp.AssertStatusCode(HttpStatusCode.BadRequest);
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
            resp.AssertStatusCode(HttpStatusCode.OK);
            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId2}/start")
                {
                    Content = new JsonContent(new BatchStartRequestModel
                    {
                        BatchFriendlyName = "Test batch 1", BatchType = BatchType.Android
                    })
                });
            resp.AssertStatusCode(HttpStatusCode.OK);

            var testFile = Path.Combine("TestFiles", "libqcbassboost.so");
            const string unifiedId = "637aa379d34ed455c314d646b8f3eaec";
            resp.AssertStatusCode(HttpStatusCode.OK);

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId1}/upload/")
                {
                    Content = new MultipartFormDataContent
                    {
                        {
                            new ByteArrayContent(await File.ReadAllBytesAsync(testFile)), testFile, Path.GetFileName(testFile)
                        }
                    }
                });
            resp.AssertStatusCode(HttpStatusCode.Created);

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId2}/upload/")
                {
                    Content = new MultipartFormDataContent
                    {
                        {
                            new ByteArrayContent(await File.ReadAllBytesAsync(testFile)), testFile, Path.GetFileName(testFile)
                        }
                    }
                });
            resp.AssertStatusCode(HttpStatusCode.AlreadyReported);

            var symbolService = _fixture.ServiceProvider!.GetRequiredService<ISymbolService>();
            var symbol = await symbolService.GetSymbol(unifiedId, CancellationToken.None);

            var batch1 = await symbolService.GetBatch(batchId1, CancellationToken.None);
            var storedSymbol = Assert.Single(batch1!.Symbols).Value!;
            Assert.Equal(symbol!.UnifiedId, storedSymbol.UnifiedId);
            Assert.Equal(symbol!.Hash, storedSymbol.Hash);

            var batch2 = await symbolService.GetBatch(batchId1, CancellationToken.None);
            storedSymbol = Assert.Single(batch2!.Symbols).Value;
            Assert.Equal(symbol!.UnifiedId, storedSymbol.UnifiedId);
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

            resp.AssertStatusCode(HttpStatusCode.OK);
            var symbolService = _fixture.ServiceProvider!.GetRequiredService<ISymbolService>();
            var batch = await symbolService.GetBatch(batchId, CancellationToken.None);
            Assert.Equal(batchId, batch!.BatchId);
            Assert.Empty(batch.Symbols);
            Assert.Equal(registration.BatchType, batch!.BatchType);
            Assert.False(batch.IsClosed);
            Assert.Equal(registration.BatchFriendlyName, batch!.FriendlyName);

            var testFile = Path.Combine("TestFiles", "libxamarin-app-arm64-v8a.so");
            const string unifiedId = "7621750937f30bf8e756cec46b960391f9f57b26";

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/upload/")
                {
                    Content = new MultipartFormDataContent
                    {
                        {
                            new ByteArrayContent(await File.ReadAllBytesAsync(testFile)), testFile, Path.GetFileName(testFile)
                        }
                    }
                });
            resp.AssertStatusCode(HttpStatusCode.Created);

            var symbol = await symbolService.GetSymbol(unifiedId, CancellationToken.None);
            Assert.Equal(Path.GetFileName(testFile), symbol!.Name);
            Assert.Equal("5fb23797a8cb482bac325eabdcb3d7e70b89fe0ec51035010e9be3a7b76fff84", symbol.Hash);
            Assert.Equal(unifiedId, symbol.UnifiedId);
            Assert.EndsWith( Path.GetFileName(testFile), symbol.Path);
            var baseWorking = _fixture.ServiceProvider!.GetRequiredService<IOptions<SymbolServiceOptions>>().Value.BaseWorkingPath;
            Assert.StartsWith(Path.Combine(baseWorking!, "processing", batch.BatchType.ToSymsorterPrefix(), batchId.ToString()), symbol.Path);
            Assert.Equal(batchId, symbol.BatchIds.Single().Key);

            Assert.Equal(FileFormat.Elf, symbol.FileFormat);
            // TODO: Add the other info
            // Assert.Equal(BuildIdType.GnuBuildId, symbol.BuildIdType);
            Assert.Equal(Architecture.Arm64, symbol.Arch);

            batch = await symbolService.GetBatch(batchId, CancellationToken.None);
            var storedSymbol = Assert.Single(batch!.Symbols).Value;
            Assert.Equal(symbol.UnifiedId, storedSymbol.UnifiedId);
            Assert.Equal(symbol.Hash, storedSymbol.Hash);
        }

        [Fact]
        public async Task IsSymbolMissing_WithoutHash_Supported()
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

            resp.AssertStatusCode(HttpStatusCode.OK);

            var testFile = Path.Combine("TestFiles", "libxamarin-app.so");
            const string unifiedId = "f59d3adfa8263dd688ad820f74a325b540dcf6b4"; // CodeId
            resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
                SymbolsController.Route + $"/batch/{batchId}/check/{unifiedId}"));
            resp.AssertStatusCode(HttpStatusCode.OK);

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/upload/")
                {
                    Content = new MultipartFormDataContent
                    {
                        {
                            new ByteArrayContent(await File.ReadAllBytesAsync(testFile)), testFile, Path.GetFileName(testFile)
                        }
                    }
                });

            resp.AssertStatusCode(HttpStatusCode.Created);

            var responseModel = await resp.Content.ToJsonElement();
            Assert.Equal(1, responseModel.GetProperty("filesCreated").GetInt32());

            // Check again if needed.
            resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
                SymbolsController.Route + $"/batch/{batchId}/check/{unifiedId}/"));
            resp.AssertStatusCode(HttpStatusCode.Conflict);
            // Check again on v2 endpoint (different status code)
            resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
                SymbolsController.Route + $"/batch/{batchId}/check/v2/{unifiedId}/"));
            resp.AssertStatusCode(HttpStatusCode.AlreadyReported);
        }

        [Fact]
        public async Task UploadSymbol_GzipedSymbol_SymbolAdded()
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

            resp.AssertStatusCode(HttpStatusCode.OK);
            var symbolService = _fixture.ServiceProvider!.GetRequiredService<ISymbolService>();
            var batch = await symbolService.GetBatch(batchId, CancellationToken.None);
            Assert.Equal(batchId, batch!.BatchId);
            Assert.Empty(batch.Symbols);
            Assert.Equal(registration.BatchType, batch!.BatchType);
            Assert.False(batch.IsClosed);
            Assert.Equal(registration.BatchFriendlyName, batch!.FriendlyName);

            var testFile = Path.Combine("TestFiles", "libxamarin-app-arm64-v8a.so");
            const string unifiedId = "7621750937f30bf8e756cec46b960391f9f57b26";

            var fileBytes = await File.ReadAllBytesAsync(testFile);
            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/upload/")
                {
                    Content = new MultipartFormDataContent
                    {
                        {
                            new GzipContent(new ByteArrayContent(fileBytes), new ClientMetrics()), testFile, Path.GetFileName(testFile)
                        }
                    }
                });
            resp.AssertStatusCode(HttpStatusCode.Created);

            var symbol = await symbolService.GetSymbol(unifiedId, CancellationToken.None);
            Assert.Equal(Path.GetFileName(testFile), symbol!.Name);
            Assert.Equal("5fb23797a8cb482bac325eabdcb3d7e70b89fe0ec51035010e9be3a7b76fff84", symbol.Hash);
            Assert.Equal(unifiedId, symbol.UnifiedId);
            Assert.EndsWith( Path.GetFileName(testFile), symbol.Path);
            var baseWorking = _fixture.ServiceProvider!.GetRequiredService<IOptions<SymbolServiceOptions>>().Value.BaseWorkingPath;
            Assert.StartsWith(Path.Combine(baseWorking!, "processing", batch.BatchType.ToSymsorterPrefix(), batchId.ToString()), symbol.Path);
            Assert.Equal(batchId, symbol.BatchIds.Single().Key);
            var actualBytes = await File.ReadAllBytesAsync(symbol.Path);
            Assert.True(fileBytes.SequenceEqual(actualBytes));

            Assert.Equal(FileFormat.Elf, symbol.FileFormat);
            // TODO: Add the other info
            // Assert.Equal(BuildIdType.GnuBuildId, symbol.BuildIdType);
            Assert.Equal(Architecture.Arm64, symbol.Arch);

            batch = await symbolService.GetBatch(batchId, CancellationToken.None);
            var storedSymbol = Assert.Single(batch!.Symbols).Value;
            Assert.Equal(symbol.UnifiedId, storedSymbol.UnifiedId);
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

            resp.AssertStatusCode(HttpStatusCode.OK);

            var testFile = Path.Combine("TestFiles", "libxamarin-app.so");
            const string unifiedId = "f59d3adfa8263dd688ad820f74a325b540dcf6b4"; // CodeId
            const string hash = "1a40a2db7c6b4dd59e3bcecd9b53cf3c7fc544afc311e25c41ee01bc4bb99a96";
            resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
                SymbolsController.Route + $"/batch/{batchId}/check/{unifiedId}/{hash}"));
            resp.AssertStatusCode(HttpStatusCode.OK);

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/upload/")
                {
                    Content = new MultipartFormDataContent
                    {
                        {
                            new ByteArrayContent(await File.ReadAllBytesAsync(testFile)), testFile, Path.GetFileName(testFile)
                        }
                    }
                });

            resp.AssertStatusCode(HttpStatusCode.Created);

            var responseModel = await resp.Content.ToJsonElement();
            Assert.Equal(1, responseModel.GetProperty("filesCreated").GetInt32());

            // Check again if needed.
            resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
                SymbolsController.Route + $"/batch/{batchId}/check/{unifiedId}/{hash}"));
            resp.AssertStatusCode(HttpStatusCode.Conflict);

            // Check again if with a different hash. API returns OK hoping the client uploads the file.
            resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
                SymbolsController.Route + $"/batch/{batchId}/check/{unifiedId}/{hash + "-wrong"}"));
            resp.AssertStatusCode(HttpStatusCode.OK);

            resp = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, SymbolsController.Route + $"/batch/{batchId}/upload/")
                {
                    Content = new MultipartFormDataContent
                    {
                        {
                            new ByteArrayContent(await File.ReadAllBytesAsync(testFile)), testFile, Path.GetFileName(testFile)
                        }
                    }
                });

            responseModel = await resp.Content.ToJsonElement();
            Assert.Equal(0, responseModel.GetProperty("filesCreated").GetInt32());
            resp.AssertStatusCode(HttpStatusCode.AlreadyReported);

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

            resp.AssertStatusCode(HttpStatusCode.NoContent);

            var symbolService = _fixture.ServiceProvider!.GetRequiredService<ISymbolService>();
            var batch = await symbolService.GetBatch(batchId, CancellationToken.None);

            Assert.True(batch!.IsClosed);
            var symbol = Assert.Single(batch.Symbols).Value;
            File.Exists(symbol.Path);

            Assert.Equal(metrics.SuccessfullyUploadCount, batch!.ClientMetrics!.SuccessfullyUploadCount);
        }
    }
}
