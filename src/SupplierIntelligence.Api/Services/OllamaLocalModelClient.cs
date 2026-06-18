using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SupplierIntelligence.Api.Contracts;
using SupplierIntelligence.Api.Options;

namespace SupplierIntelligence.Api.Services;

public sealed class OllamaLocalModelClient(
    HttpClient httpClient,
    IOptions<LocalModelOptions> options) : ILocalModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalModelOptions options = options.Value;

    public string Provider => options.Provider;
    public string BaseUrl => options.BaseUrl;
    public string DefaultModel => options.Model;

    public async Task<IReadOnlyList<LocalModelInfo>> GetModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<OllamaTagsResponse>(
                "api/tags",
                JsonOptions,
                cancellationToken);

            return response?.Models
                .Select(model => new LocalModelInfo(
                    model.Name,
                    model.Size,
                    model.ModifiedAt))
                .OrderBy(model => model.Name)
                .ToList() ?? [];
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new LocalModelException(
                $"Could not reach Ollama at {BaseUrl}. Start Ollama and make sure a model is pulled.",
                exception);
        }
    }

    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        _ = await ChatAsync(
            DefaultModel,
            "You validate the local model runtime.",
            "Reply with OK only.",
            cancellationToken);
    }

    public async Task<string> ChatAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var request = new OllamaChatRequest(
            model,
            [
                new OllamaMessage("system", systemPrompt),
                new OllamaMessage("user", userPrompt)
            ],
            Stream: false,
            Options: new OllamaOptions(options.Temperature));

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "api/chat",
                request,
                JsonOptions,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new LocalModelException(
                    $"Ollama returned HTTP {(int)response.StatusCode}: {body}");
            }

            var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(body, JsonOptions);
            var content = chatResponse?.Message?.Content;

            return string.IsNullOrWhiteSpace(content)
                ? throw new LocalModelException("Ollama returned an empty chat response.")
                : content.Trim();
        }
        catch (LocalModelException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new LocalModelException(
                $"Could not reach Ollama at {BaseUrl}. Start Ollama and make sure model '{model}' is available.",
                exception);
        }
    }

    private sealed record OllamaTagsResponse(IReadOnlyList<OllamaModel> Models);

    private sealed record OllamaModel(
        string Name,
        long Size,
        [property: JsonPropertyName("modified_at")] DateTimeOffset? ModifiedAt);

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaMessage> Messages,
        bool Stream,
        OllamaOptions Options);

    private sealed record OllamaMessage(
        string Role,
        string Content);

    private sealed record OllamaOptions(
        double Temperature);

    private sealed record OllamaChatResponse(
        OllamaMessage? Message,
        bool Done);
}
