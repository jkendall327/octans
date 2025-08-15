using System.Collections.ObjectModel;
using System.Threading.Channels;

namespace Octans.Tests;

/// <summary>
/// Channel that lets us see what items are written to it for the sake of testing.
/// </summary>
public sealed class SpyChannelWriter<T> : ChannelWriter<T>
{
    public Channel<T> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<T>();
    public ICollection<T> WrittenItems { get; } = new Collection<T>();

    public override bool TryWrite(T item)
    {
        WrittenItems.Add(item);

        return Channel.Writer.TryWrite(item);
    }

    public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
    {
        return Channel.Writer.WaitToWriteAsync(cancellationToken);
    }
}