using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace SymbolCollector.Core.Tests
{
    public class ClientTests
    {
        private class Fixture
        {
            public Uri ServiceUri { get; set; } = new Uri("https://test.sentry/");

            public ObjectFileParser ObjectFileParser { get; set; } =
                new ObjectFileParser(logger: Substitute.For<ILogger<ObjectFileParser>>());

            public HttpMessageHandler? HttpMessageHandler { get; set; }
            public AssemblyName? AssemblyName { get; set; }
            public int? ParallelTasks { get; set; }
            public HashSet<string>? BlackListedPaths { get; set; }
            public ClientMetrics? Metrics { get; set; }
            public ISymbolClient SymbolClient { get; set; } = Substitute.For<ISymbolClient>();
            public ILogger<Client>? Logger { get; set; }

            public Client GetSut() =>
                new Client(
                    SymbolClient,
                    ObjectFileParser,
                    ParallelTasks,
                    BlackListedPaths,
                    Metrics,
                    Logger);
        }

        private readonly Fixture _fixture = new Fixture();

        [Fact]
        public async Task UploadAllPathsAsync_TestFilesDirectory_FilesDetected()
        {
            var counter = 0;
            _fixture.ObjectFileParser = new ObjectFileParser(new FatBinaryReader());
            _fixture.HttpMessageHandler = new TestMessageHandler((message, token) =>
            {
                if (message.RequestUri.PathAndQuery.EndsWith("upload"))
                {
                    Interlocked.Increment(ref counter);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            });
            _fixture.SymbolClient = new SymbolClient(
                _fixture.ServiceUri,
                Substitute.For<ILogger<SymbolClient>>(),
                _fixture.HttpMessageHandler);

            var sut = _fixture.GetSut();
            await sut.UploadAllPathsAsync(new[] {"TestFiles"}, CancellationToken.None);
            Assert.Equal(12, counter);
            // TODO: Match exact files and their debug ids.
        }

        [Fact]
        public void ParallelTasks_DefaultValue_Ten()
        {
            var sut = _fixture.GetSut();
            Assert.Equal(10, sut.ParallelTasks);
        }

        private class TestMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _callback;

            public TestMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
                => _callback = callback;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
                => _callback(request, cancellationToken);
        }
    }
}
