using System.Threading.Channels;
using FluentAssertions;
using Xunit;

namespace Sl4n.Tests;

public sealed class WorkerDisposeTests
{
    [Fact]
    public async Task DisposeAsync_IsIdempotent_SoDoubleDisposalCannotCrashShutdown()
    {
        // The worker is registered both as a singleton and as its own IHostedService —
        // two DI descriptors, one instance — so the ServiceProvider disposes it TWICE.
        // Before the fix the second pass hit the disposed CTS and a clean host shutdown
        // crashed with ObjectDisposedException (found by the AOT smoke).
        Channel<RawLogEvent> channel = Channel.CreateUnbounded<RawLogEvent>();
        Sl4nTransportWorker worker = new(
            channel.Reader, [], MaskingEngine.Create(new MaskingConfig()));

        await worker.StartAsync(CancellationToken.None);
        await worker.DisposeAsync();

        Func<Task> second = async () => await worker.DisposeAsync();
        await second.Should().NotThrowAsync();
    }
}
