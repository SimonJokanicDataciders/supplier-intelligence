using System.Threading.Channels;

namespace SupplierIntelligence.Api.Services;

public sealed class ChannelSupplierAnalysisQueue : ISupplierAnalysisQueue
{
    private readonly Channel<int> channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(int analysisJobId, CancellationToken cancellationToken = default)
    {
        return channel.Writer.WriteAsync(analysisJobId, cancellationToken);
    }

    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken cancellationToken)
    {
        return channel.Reader.ReadAllAsync(cancellationToken);
    }
}
