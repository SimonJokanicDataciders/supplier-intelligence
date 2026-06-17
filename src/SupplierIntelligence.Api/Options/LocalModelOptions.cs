namespace SupplierIntelligence.Api.Options;

public sealed class LocalModelOptions
{
    public string Provider { get; set; } = "OpenRouter";
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string OpenRouterBaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "openrouter/free";
    public double Temperature { get; set; } = 0.2;
    public int TimeoutSeconds { get; set; } = 120;
    public bool EnableWebSearch { get; set; } = true;
    public int SearchMaxResults { get; set; } = 8;
    public int SearchMaxTotalResults { get; set; } = 20;
    public string[] SearchExcludedDomains { get; set; } = ["wikipedia.org", "wikidata.org"];
}
