using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Object = Google.Apis.Storage.v1.Data.Object;

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

            await target.WriteAsync("name", new MemoryStream(), None);

            await _fixture.StorageClientFactory.Received().Create();
        }

        [Fact]
        public async Task Write_ConcurrentCalls_FollowUpCallsDisposeClient()
        {
            const int concurrentCalls = 10;
            var sync = new ManualResetEventSlim(false);
            var clientFactory = new StubStorageClientFactory(sync, concurrentCalls);
            _fixture.StorageClientFactory = clientFactory;

            var target = _fixture.GetSut();

            var tasks = Enumerable.Range(0, concurrentCalls)
                .Select(i => Task.Run(async () => await target.WriteAsync(i.ToString(), new MemoryStream(), None))).ToList();

            await Task.WhenAll(tasks);
            sync.Wait();

            Assert.Equal(concurrentCalls, clientFactory.CallCount);
            // All but 1 got disposed
            Assert.Equal(concurrentCalls - 1, clientFactory.Clients.Count(m => m.DisposedCalled));
            Assert.Equal(1, clientFactory.Clients.Count(m => m.UploadObjectAsyncCalled));
        }

        private class StubStorageClientFactory : IStorageClientFactory
        {
            private readonly ManualResetEventSlim _sync;
            private readonly int _unblockAt;
            private int _callCounter;
            private readonly ConcurrentBag<SubClient> _clients = new ConcurrentBag<SubClient>();

            public int CallCount => _callCounter;
            public IEnumerable<SubClient> Clients => _clients;

            public StubStorageClientFactory(ManualResetEventSlim sync, int unblockAt)
            {
                _sync = sync;
                _unblockAt = unblockAt;
            }

            public Task<StorageClient> Create()
            {
                var mock = new SubClient();
                _clients.Add(mock);
                if (Interlocked.Increment(ref _callCounter) == _unblockAt)
                {
                    _sync.Set();
                }
                else
                {
                    // Block all calls until the last one arrives.
                    _sync.Wait();
                }

                return Task.FromResult((StorageClient) mock);
            }
        }

        private class SubClient : StorageClient
        {
            public bool UploadObjectAsyncCalled { get; set; }
            public bool DisposedCalled { get; set; }

            public override Task<Object> UploadObjectAsync(
                string bucket,
                string objectName,
                string contentType,
                Stream source,
                UploadObjectOptions? options = null,
                CancellationToken cancellationToken = default,
                IProgress<IUploadProgress>? progress = null)
            {
                UploadObjectAsyncCalled = true;
                return Task.FromResult(new Object());
            }

            public override void Dispose()
            {
                DisposedCalled = true;
                base.Dispose();
            }
        }
    }
}
