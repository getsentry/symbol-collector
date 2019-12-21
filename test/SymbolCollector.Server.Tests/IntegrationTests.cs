using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Upload;
using Google.Cloud.Storage.V1;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace SymbolCollector.Server.Tests
{
    public class IntegrationTests : IClassFixture<WebApplicationFactory<Startup>>
    {
        private readonly WebApplicationFactory<Startup> _factory;

        public IntegrationTests(WebApplicationFactory<Startup> factory) => _factory = factory;

        [Fact]
        public async Task Post_StoresFileInGcs()
        {
            var mockStorageClient = Substitute.For<StorageClient>();
            var mockFactory = Substitute.For<IStorageClientFactory>();
            mockFactory.Create().Returns(Task.FromResult(mockStorageClient));

            var client = _factory.WithWebHostBuilder(c => c.ConfigureServices(s => s.AddSingleton(mockFactory))).CreateClient();
            const string expectedFileName = "libfake.so";
            const string expectedDebugId = "982391283";

            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/image")
            {
                Headers = { {"debug-id", expectedDebugId} },
                Content = new MultipartFormDataContent {{new StringContent("fake stuff"), expectedFileName, expectedFileName}}
            });

            Assert.True(resp.IsSuccessStatusCode);
            await mockStorageClient.Received(1).UploadObjectAsync(
                Arg.Any<string>(),
                expectedFileName,
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<UploadObjectOptions>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<IProgress<IUploadProgress>>());
        }
    }
}
