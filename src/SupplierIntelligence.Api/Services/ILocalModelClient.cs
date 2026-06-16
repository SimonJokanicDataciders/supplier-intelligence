using SupplierIntelligence.Api.Contracts;

namespace SupplierIntelligence.Api.Services;

public interface ILocalModelClient
{
    string Provider { get; }
    string BaseUrl { get; }
    string DefaultModel { get; }

    Task<IReadOnlyList<LocalModelInfo>> GetModelsAsync(CancellationToken cancellationToken);

    Task<string> ChatAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);
}
