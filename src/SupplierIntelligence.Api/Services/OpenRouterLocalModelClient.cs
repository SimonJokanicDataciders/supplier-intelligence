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
    IOptions<LocalModelOptions> options,
    OpenRouterRuntimeSettings runtimeSettings) : ILocalModelClient
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
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    throw new LocalModelException(
                        BuildAuthenticationFailureMessage(body));
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
                $"Could not reach OpenRouter at {BaseUrl}. Check network access, then use Test connection with the runtime key saved in Technical details.",
                exception);
        }
    }

    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        _ = await SendChatAsync(
            DefaultModel,
            "You validate the OpenRouter runtime.",
            "Reply with OK only.",
            includeTools: false,
            maxTokens: 8,
            requireContent: false,
            cancellationToken);
    }

    public async Task<string> ChatAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        return await SendChatAsync(
            model,
            systemPrompt,
            userPrompt,
            includeTools: true,
            maxTokens: null,
            requireContent: true,
            cancellationToken);
    }

    private async Task<string> SendChatAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        bool includeTools,
        int? maxTokens,
        bool requireContent,
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
            includeTools ? BuildTools() : null,
            maxTokens);

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
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    throw new LocalModelException(
                        BuildAuthenticationFailureMessage(body));
                }

                throw new LocalModelException(
                    $"OpenRouter returned HTTP {(int)response.StatusCode}: {body}");
            }

            var content = ExtractChatContent(body);

            if (!requireContent)
            {
                return string.IsNullOrWhiteSpace(content) ? "OK" : content.Trim();
            }

            return string.IsNullOrWhiteSpace(content)
                ? throw new LocalModelException($"OpenRouter returned an empty chat response: {TrimForMessage(body)}")
                : content.Trim();
        }
        catch (LocalModelException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new LocalModelException(
                $"Could not reach OpenRouter at {BaseUrl}. Check network access, then use Test connection with the runtime key saved in Technical details.",
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
        var apiKey = ResolveApiKey();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new LocalModelException("OpenRouter is selected, but no runtime API key is configured. Paste a key in Technical details and save it for this run.");
        }

        httpClient.DefaultRequestHeaders.Authorization = null;
        httpClient.DefaultRequestHeaders.Remove("HTTP-Referer");
        httpClient.DefaultRequestHeaders.Remove("X-OpenRouter-Title");

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", "http://localhost");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-OpenRouter-Title", "Supplier Intelligence Learning App");
    }

    private string? ResolveApiKey()
    {
        var runtimeApiKey = runtimeSettings.GetSnapshot().ApiKey;
        return string.IsNullOrWhiteSpace(runtimeApiKey)
            ? null
            : OpenRouterRuntimeSettings.SanitizeApiKey(runtimeApiKey);
    }

    private static string BuildAuthenticationFailureMessage(string body)
    {
        var detail = ExtractOpenRouterError(body);

        return string.IsNullOrWhiteSpace(detail)
            ? "OpenRouter rejected the runtime key saved in Technical details. Clear it, save a fresh OpenRouter key, then use Test connection before rerunning analysis."
            : $"OpenRouter rejected the runtime key saved in Technical details: {detail}";
    }

    private static string ExtractOpenRouterError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? string.Empty;
                }

                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString() ?? string.Empty;
                }
            }

            if (root.TryGetProperty("message", out var rootMessage) &&
                rootMessage.ValueKind == JsonValueKind.String)
            {
                return rootMessage.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            return body.Length > 240 ? $"{body[..240]}..." : body;
        }

        return string.Empty;
    }

    private static string ExtractChatContent(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return string.Empty;
            }

            var firstChoice = choices[0];

            if (!firstChoice.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("content", out var content))
            {
                return string.Empty;
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }

            if (content.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            return string.Join(
                Environment.NewLine,
                content.EnumerateArray()
                    .Select(ReadContentPart)
                    .Where(part => !string.IsNullOrWhiteSpace(part)));
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string ReadContentPart(JsonElement part)
    {
        if (part.ValueKind == JsonValueKind.String)
        {
            return part.GetString() ?? string.Empty;
        }

        if (part.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (part.TryGetProperty("text", out var text) &&
            text.ValueKind == JsonValueKind.String)
        {
            return text.GetString() ?? string.Empty;
        }

        if (part.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string TrimForMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "response body was empty";
        }

        return body.Length > 300 ? $"{body[..300]}..." : body;
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
        IReadOnlyList<OpenRouterTool>? Tools,
        [property: JsonPropertyName("max_tokens")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int? MaxTokens);

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

}
