using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace SymbolCollector.Core.Tests;

public class ClientTests
{
    private class Fixture
    {
        public Uri ServiceUri { get; set; } = new Uri("https://test.sentry/");

        public ObjectFileParser ObjectFileParser { get; set; }
        public ObjectFileParserOptions ObjectFileParserOptions { get; set; } = new ObjectFileParserOptions();

        public HttpMessageHandler? HttpMessageHandler { get; set; }
        public ClientMetrics Metrics { get; set; } = new ClientMetrics();
        public ISymbolClient SymbolClient { get; set; } = Substitute.For<ISymbolClient>();

        public SymbolClientOptions SymbolClientOptions { get; set; } = new SymbolClientOptions
        {
            BaseAddress = new Uri("https://test.sentry/"),
        };

        public ILogger<Client> Logger { get; set; } = Substitute.For<ILogger<Client>>();

        public Fixture()
        {
            HttpMessageHandler = new TestMessageHandler((message, token) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)));
            ObjectFileParser = new ObjectFileParser(Metrics,
                Options.Create(ObjectFileParserOptions),
                Substitute.For<ILogger<ObjectFileParser>>());
        }

        public Client GetSut() =>
            new Client(
                SymbolClient,
                ObjectFileParser,
                SymbolClientOptions,
                Metrics,
                Logger);
    }

    private readonly Fixture _fixture = new Fixture();

    [Fact]
    public async Task UploadAllPathsAsync_TestFilesDirectory_FilesDetected()
    {
        var counter = 0;
        _fixture.ObjectFileParser = new ObjectFileParser(_fixture.Metrics,
            Options.Create(new ObjectFileParserOptions()),
            Substitute.For<ILogger<ObjectFileParser>>(), new FatBinaryReader());
        _fixture.HttpMessageHandler = new TestMessageHandler((message, token) =>
        {
            if (message.RequestUri!.PathAndQuery.EndsWith("upload"))
            {
                Interlocked.Increment(ref counter);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        });
        _fixture.SymbolClient = new SymbolClient(
            Substitute.For<IHub>(),
            new SymbolClientOptions {BaseAddress = _fixture.ServiceUri, UserAgent = "UnitTest/0.0.0"},
            new ClientMetrics(),
            Substitute.For<ILogger<SymbolClient>>(),
            new HttpClient(_fixture.HttpMessageHandler));

        var sut = _fixture.GetSut();
        await sut.UploadAllPathsAsync("friendly name", BatchType.IOS, new[] {"TestFiles"}, SentrySdk.StartTransaction("test", "test-op"), CancellationToken.None);

        // number of valid test files in TestFiles
        Assert.Equal(12, counter);
    }

    [Fact]
    public async Task UploadAllPathsAsync_TestFilesDirectory_FileCorrectlySent()
    {
        _fixture.ObjectFileParser = new ObjectFileParser(_fixture.Metrics,
            Options.Create(new ObjectFileParserOptions()),
            Substitute.For<ILogger<ObjectFileParser>>(), new FatBinaryReader());

        var sut = _fixture.GetSut();
        await sut.UploadAllPathsAsync("friendly name", BatchType.IOS, new[] {"TestFiles"}, SentrySdk.StartTransaction("test", "test-op"), CancellationToken.None);

        // Make sure all valid test files were picked up
        var testFiles = new ObjectFileResultTestCases()
            .Select(c => c[0])
            .OfType<ObjectFileResultTestCase>()
            .Where(c => c.Expected is {})
            .SelectMany(c => c.Expected is FatMachOFileResult fatMachOFileResult
                ? fatMachOFileResult.InnerFiles
                : new List<ObjectFileResult> {c.Expected!})
            .ToList();

        foreach (var testFile in testFiles)
        {
            await _fixture.SymbolClient.Received(1).Upload(
                Arg.Any<Guid>(),
                testFile.UnifiedId,
                testFile.Hash,
                Path.GetFileName(testFile.Path),
                Arg.Any<Func<Stream>>(),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public void ParallelTasks_DefaultValue_Ten()
    {
        var sut = _fixture.GetSut();
        Assert.Equal(10, sut.ParallelTasks);
    }
}
