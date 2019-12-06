using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Upload;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using static NSubstitute.Substitute;
using static System.Threading.CancellationToken;

namespace SymbolCollector.Server.Tests
{
    public class SymbolGcsWriterTests
    {
        private class Fixture
        {
            public ILogger<SymbolGcsWriter> Logger { get; set; } = For<ILogger<SymbolGcsWriter>>();
            public IStorageClientFactory StorageClientFactory { get; set; } = For<IStorageClientFactory>();

            public SymbolGcsWriter GetSut() => new SymbolGcsWriter(StorageClientFactory, Logger);
        }

        readonly Fixture _fixture = new Fixture();

        [Fact]
        public async Task Write_FirstCall_CreatesStorageClient()
        {
            var target = _fixture.GetSut();

            await target.WriteAsync( "name", new MemoryStream(), None);

            await _fixture.StorageClientFactory.Received().Create();
        }

        [Fact]
        public async Task Write_ConcurrentCalls_FollowUpCallsDisposeClient()
        {
            var evt = new ManualResetEventSlim();
            int callCount = 0;
            var concurrentCalls = 5;
            var mocks = new ConcurrentBag<StorageClient>();

            _fixture.StorageClientFactory.Create().Returns(_ =>
            {
                var mock = For<StorageClient>();
                mocks.Add(mock);
                if (++callCount < concurrentCalls)
                {
                    evt.Wait();
                }
                else
                {
                    evt.Set();
                }

                return Task.FromResult(mock);
            });

            var target = _fixture.GetSut();

            var tasks = Enumerable.Range(0, concurrentCalls)
                .Select(i => Task.Run(async () => await target.WriteAsync(i.ToString(), new MemoryStream(), None)));

            await Task.WhenAll(tasks);

            await _fixture.StorageClientFactory.Received(concurrentCalls).Create();
            // All but 1 got disposed
            Assert.Equal(concurrentCalls - 1, mocks.Count(m => DidReceiveCall(() => m.Received(1).Dispose())));

            foreach (var mock in mocks)
            {
                var timesCalled = concurrentCalls;
                if (DidReceiveCall(() => mock.Received(1).Dispose()))
                {
                    timesCalled = 0;
                }

                await mock.Received(timesCalled).UploadObjectAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<Stream>(),
                    Arg.Any<UploadObjectOptions>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<IProgress<IUploadProgress>>());
            }

            bool DidReceiveCall(Action call)
            {
                try
                {
                    call();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
