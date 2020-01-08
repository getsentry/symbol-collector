using System;
using System.Collections.Concurrent;
using SymbolCollector.Core;

namespace SymbolCollector.Server.Models
{
    // https://github.com/getsentry/symbolicator/blob/cd545b3bdbb7c3a0869de20c387740baced2be5c/symsorter/src/app.rs

    public class SymbolUploadBatch
    {
        public Guid BatchId { get; }
        public DateTimeOffset StartTime { get; }
        public DateTimeOffset? EndTime { get; private set; }

        // Will be used as BundleId (caller doesn't need to worry about it being unique).
        public string FriendlyName { get; }

        public BatchType BatchType { get; }

        public ConcurrentDictionary<string, SymbolMetadata> Symbols { get; } =
            new ConcurrentDictionary<string, SymbolMetadata>();

        public IClientMetrics? ClientMetrics { get; set; }

        public bool IsClosed => EndTime.HasValue;

        public SymbolUploadBatch(Guid batchId, string friendlyName, BatchType batchType)
        {
            if (batchId == default)
            {
                throw new ArgumentException("Empty Batch Id.");
            }

            if (string.IsNullOrWhiteSpace(friendlyName))
            {
                throw new ArgumentException("Friendly name is required.");
            }

            if (batchType == BatchType.Unknown)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(batchType),
                    batchType,
                    "A batch type is required.");
            }

            BatchId = batchId;
            FriendlyName = friendlyName;
            BatchType = batchType;
            StartTime = DateTimeOffset.UtcNow;
        }

        public void Close()
        {
            if (EndTime.HasValue)
            {
                throw new InvalidOperationException(
                    $"Can't close batch '{BatchId}'. It was already closed at {EndTime}.");
            }

            EndTime = DateTimeOffset.UtcNow;
        }
    }
}
