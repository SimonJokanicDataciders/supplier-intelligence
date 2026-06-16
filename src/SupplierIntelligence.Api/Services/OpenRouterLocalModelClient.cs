using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SupplierIntelligence.Api.Contracts;
using SupplierIntelligence.Api.Options;

namespace SupplierIntelligence.Api.Services;

public sealed class OpenRouterLocalModelClient(
    HttpClient httpClient,
    IOptions<LocalModelOptions> options) : ILocalModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalModelOptions options = options.Value;

    public string Provider => "OpenRouter";
    public string BaseUrl => options.OpenRouterBaseUrl;
    public string DefaultModel => IsOllamaDefault(options.Model)
        ? "openrouter/free"
        : options.Model;

    public async Task<IReadOnlyList<LocalModelInfo>> GetModelsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            using var response = await httpClient.GetAsync("models", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new LocalModelException(
                        "OpenRouter rejected the API key or account. Create a fresh key in OpenRouter, export OPENROUTER_API_KEY again, restart the C# API, and rerun the analysis.");
                }

                throw new LocalModelException(
                    $"OpenRouter returned HTTP {(int)response.StatusCode}: {body}");
            }

            var modelResponse = JsonSerializer.Deserialize<OpenRouterModelsResponse>(body, JsonOptions);

            return modelResponse?.Data
                .Where(model => model.Id.EndsWith(":free", StringComparison.OrdinalIgnoreCase) ||
                    model.Id.Equals("openrouter/free", StringComparison.OrdinalIgnoreCase))
                .Select(model => new LocalModelInfo(
                    model.Id,
                    0,
                    model.Created is null
                        ? null
                        : DateTimeOffset.FromUnixTimeSeconds(model.Created.Value)))
                .OrderBy(model => model.Name)
                .ToList() ?? [];
        }
        catch (LocalModelException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new LocalModelException(
                $"Could not reach OpenRouter at {BaseUrl}. Check network access and OPENROUTER_API_KEY.",
                exception);
        }
    }

    public async Task<string> ChatAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var selectedModel = string.IsNullOrWhiteSpace(model) || IsOllamaDefault(model)
            ? DefaultModel
            : model.Trim();

        var request = new OpenRouterChatRequest(
            selectedModel,
            [
                new OpenRouterMessage("system", systemPrompt),
                new OpenRouterMessage("user", userPrompt)
            ],
            options.Temperature,
            BuildTools());

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "chat/completions",
                request,
                JsonOptions,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new LocalModelException(
                        "OpenRouter rejected the API key or account. Create a fresh key in OpenRouter, export OPENROUTER_API_KEY again, restart the C# API, and rerun the analysis.");
                }

                throw new LocalModelException(
                    $"OpenRouter returned HTTP {(int)response.StatusCode}: {body}");
            }

            var chatResponse = JsonSerializer.Deserialize<OpenRouterChatResponse>(body, JsonOptions);
            var content = chatResponse?.Choices.FirstOrDefault()?.Message?.Content;

            return string.IsNullOrWhiteSpace(content)
                ? throw new LocalModelException("OpenRouter returned an empty chat response.")
                : content.Trim();
        }
        catch (LocalModelException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new LocalModelException(
                $"Could not reach OpenRouter at {BaseUrl}. Check network access and OPENROUTER_API_KEY.",
                exception);
        }
    }

    private IReadOnlyList<OpenRouterTool>? BuildTools()
    {
        if (!options.EnableWebSearch)
        {
            return null;
        }

        return
        [
            new OpenRouterTool(
                "openrouter:web_search",
                new OpenRouterWebSearchParameters(
                    "exa",
                    Math.Clamp(options.SearchMaxResults, 1, 25),
                    Math.Max(options.SearchMaxTotalResults, options.SearchMaxResults),
                    "medium",
                    options.SearchExcludedDomains
                        .Where(domain => !string.IsNullOrWhiteSpace(domain))
                        .Select(domain => domain.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
        ];
    }

    private void EnsureConfigured()
    {
        var apiKey = string.IsNullOrWhiteSpace(options.ApiKey)
            ? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            : options.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new LocalModelException("OpenRouter is selected, but no API key is configured. Set LocalModel:ApiKey or OPENROUTER_API_KEY.");
        }

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", "http://localhost");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Title", "Supplier Intelligence Learning App");
    }

    private static bool IsOllamaDefault(string model)
    {
        return model.Equals("llama3.2:3b", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record OpenRouterModelsResponse(IReadOnlyList<OpenRouterModel> Data);

    private sealed record OpenRouterModel(
        string Id,
        long? Created);

    private sealed record OpenRouterChatRequest(
        string Model,
        IReadOnlyList<OpenRouterMessage> Messages,
        double Temperature,
        IReadOnlyList<OpenRouterTool>? Tools);

    private sealed record OpenRouterMessage(
        string Role,
        string Content);

    private sealed record OpenRouterTool(
        string Type,
        OpenRouterWebSearchParameters Parameters);

    private sealed record OpenRouterWebSearchParameters(
        string Engine,
        [property: JsonPropertyName("max_results")] int MaxResults,
        [property: JsonPropertyName("max_total_results")] int MaxTotalResults,
        [property: JsonPropertyName("search_context_size")] string SearchContextSize,
        [property: JsonPropertyName("excluded_domains")] IReadOnlyList<string> ExcludedDomains);

    private sealed record OpenRouterChatResponse(
        IReadOnlyList<OpenRouterChoice> Choices);

    private sealed record OpenRouterChoice(
        OpenRouterMessage? Message);
}
