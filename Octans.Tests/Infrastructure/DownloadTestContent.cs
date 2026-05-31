using System.Net;

namespace Octans.Tests.Infrastructure;

internal sealed class PausingReadContent : StreamContent
{
    private readonly PausingReadStream _stream;

    public PausingReadContent(string body) : this(new PausingReadStream(body))
    {
    }

    private PausingReadContent(PausingReadStream stream) : base(stream)
    {
        _stream = stream;
    }

    public TaskCompletionSource SecondReadStarted => _stream.SecondReadStarted;

    public bool ThrowOnSecondRead
    {
        get => _stream.ThrowOnSecondRead;
        set => _stream.ThrowOnSecondRead = value;
    }

    public void ReleaseSecondRead()
    {
        _stream.ReleaseSecondRead();
    }
}

internal sealed class PausingReadStream(string body) : Stream
{
    private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(body);
    private readonly TaskCompletionSource _continueSecondRead =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _position;
    private bool _secondReadStarted;

    public TaskCompletionSource SecondReadStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool ThrowOnSecondRead { get; set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _bytes.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= _bytes.Length)
        {
            return 0;
        }

        if (_position == 0)
        {
            var firstChunkLength = Math.Min(2, _bytes.Length);
            _bytes.AsMemory(0, firstChunkLength).CopyTo(buffer);
            _position += firstChunkLength;
            return firstChunkLength;
        }

        if (!_secondReadStarted)
        {
            _secondReadStarted = true;
            SecondReadStarted.TrySetResult();

            if (ThrowOnSecondRead)
            {
                throw new IOException("stream failed after partial body write");
            }

            await _continueSecondRead.Task.WaitAsync(cancellationToken);
        }

        var bytesRemaining = _bytes.Length - _position;
        var bytesToRead = Math.Min(buffer.Length, bytesRemaining);
        _bytes.AsMemory(_position, bytesToRead).CopyTo(buffer);
        _position += bytesToRead;

        return bytesToRead;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public void ReleaseSecondRead()
    {
        _continueSecondRead.TrySetResult();
    }
}

internal sealed class UnknownLengthContent(byte[] body) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return stream.WriteAsync(body).AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
