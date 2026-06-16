namespace SupplierIntelligence.Api.Services;

public interface ISupplierAnalysisQueue
{
    ValueTask EnqueueAsync(int analysisJobId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<int> ReadAllAsync(CancellationToken cancellationToken);
}
