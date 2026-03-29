using System.Threading.Channels;

namespace Sl4n;

internal static class Sl4nChannel
{
    public static Channel<RawLogEvent> Create(int capacity = 4096) =>
        Channel.CreateBounded<RawLogEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
}
