using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SymbolCollector.Core;

public class CounterStream : Stream
{
    private readonly Stream _streamImplementation;
    private readonly ClientMetrics _metrics;

    public CounterStream(Stream streamImplementation, ClientMetrics metrics)
    {
        _streamImplementation = streamImplementation;
        _metrics = metrics;
    }

    public override void Flush() => _streamImplementation.Flush();

    public override int Read(byte[] buffer, int offset, int count) => _streamImplementation.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => _streamImplementation.Seek(offset, origin);

    public override void SetLength(long value) => _streamImplementation.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        _streamImplementation.Write(buffer, offset, count);
        _metrics.UploadedBytesAdd(count);
    }

    public override bool CanRead => _streamImplementation.CanRead;

    public override bool CanSeek => _streamImplementation.CanSeek;

    public override bool CanWrite => _streamImplementation.CanWrite;

    public override long Length => _streamImplementation.Length;

    public override long Position
    {
        get => _streamImplementation.Position;
        set => _streamImplementation.Position = value;
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
        return _streamImplementation.BeginRead(buffer, offset, count, callback, state);
    }

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
        return _streamImplementation.BeginWrite(buffer, offset, count, callback, state);
    }

    public override void Close()
    {
        _streamImplementation.Close();
    }

    public override void CopyTo(Stream destination, int bufferSize)
    {
        _streamImplementation.CopyTo(destination, bufferSize);
    }

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        return _streamImplementation.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    public override ValueTask DisposeAsync()
    {
        return _streamImplementation.DisposeAsync();
    }

    public override int EndRead(IAsyncResult asyncResult)
    {
        return _streamImplementation.EndRead(asyncResult);
    }

    public override void EndWrite(IAsyncResult asyncResult)
    {
        _streamImplementation.EndWrite(asyncResult);
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _streamImplementation.FlushAsync(cancellationToken);
    }

    public override int Read(Span<byte> buffer)
    {
        return _streamImplementation.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _streamImplementation.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        return _streamImplementation.ReadAsync(buffer, cancellationToken);
    }

    public override int ReadByte()
    {
        return _streamImplementation.ReadByte();
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _streamImplementation.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _metrics.UploadedBytesAdd(count);
        return _streamImplementation.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        _metrics.UploadedBytesAdd(buffer.Length);
        return _streamImplementation.WriteAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
        _metrics.UploadedBytesAdd(1);
        _streamImplementation.WriteByte(value);
    }

    public override bool CanTimeout { get; }
    public override int ReadTimeout { get; set; }
    public override int WriteTimeout { get; set; }
    public override object InitializeLifetimeService()
    {
        return _streamImplementation.InitializeLifetimeService();
    }

    public override bool Equals(object obj)
    {
        return _streamImplementation.Equals(obj);
    }

    public override int GetHashCode()
    {
        return _streamImplementation.GetHashCode();
    }

    public override string ToString()
    {
        return _streamImplementation.ToString();
    }
}
