using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;

namespace SymbolCollector.Server
{
    public class GoogleCloudStorageOptions
    {
        public GoogleCredential Credential { get; set; } = null!; // Filled and validated by the Configuration system
        public string BucketName { get; set; } = null!;
    }

    public class StorageClientFactory : IStorageClientFactory
    {
        private readonly GoogleCloudStorageOptions _options;

        public StorageClientFactory(GoogleCloudStorageOptions options) =>
            _options = options ?? throw new ArgumentNullException(nameof(options));

        public Task<StorageClient> Create() => StorageClient.CreateAsync(_options.Credential);
    }

    public interface IStorageClientFactory
    {
        Task<StorageClient> Create();
    }

    public interface ISymbolGcsWriter
    {
        Task WriteAsync(string name, Stream data, CancellationToken cancellationToken);
    }

    public class SymbolGcsWriter : ISymbolGcsWriter
    {
        private readonly IStorageClientFactory _storageClientFactory;
        private readonly ILogger<SymbolGcsWriter> _logger;
        private volatile StorageClient? _storageClient;
        private const string BucketName = "sentry-android-symbols-1";

        public SymbolGcsWriter(IStorageClientFactory storageClientFactory, ILogger<SymbolGcsWriter> logger)
        {
            _storageClientFactory =
                storageClientFactory ?? throw new ArgumentNullException(nameof(storageClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task WriteAsync(string name, Stream data, CancellationToken cancellationToken)
        {
            if (_storageClient is null)
            {
                var storageClient = await _storageClientFactory.Create();
                Interlocked.CompareExchange(ref _storageClient, storageClient, null);
                if (!ReferenceEquals(_storageClient, storageClient))
                {
                    storageClient.Dispose();
                }
            }

            var obj = await _storageClient!.UploadObjectAsync(
                bucket: BucketName,
                objectName: name,
                contentType: "application/octet-stream",
                source: data,
                options: new UploadObjectOptions { PredefinedAcl = PredefinedObjectAcl.PublicRead },
                cancellationToken: cancellationToken);

            _logger.LogInformation("Symbol {name} with {length} length stored {link}", name, data.Length, obj.MediaLink);
        }
    }
}
