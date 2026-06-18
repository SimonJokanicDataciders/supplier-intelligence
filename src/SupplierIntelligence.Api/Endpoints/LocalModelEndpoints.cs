using SupplierIntelligence.Api.Contracts;
using SupplierIntelligence.Api.Services;

namespace SupplierIntelligence.Api.Endpoints;

public static class LocalModelEndpoints
{
    public static IEndpointRouteBuilder MapLocalModelEndpoints(this IEndpointRouteBuilder app)
    {
        var localModels = app.MapGroup("/api/local-models")
            .WithTags("Local Models");

        localModels.MapGet("/", async (
            ILocalModelClient localModel,
            OpenRouterRuntimeSettings openRouterRuntimeSettings,
            CancellationToken cancellationToken) =>
        {
            var apiKeyState = GetOpenRouterApiKeyState(openRouterRuntimeSettings);

            try
            {
                var models = await localModel.GetModelsAsync(cancellationToken);
                await localModel.ValidateAsync(cancellationToken);

                return Results.Ok(new LocalModelStatusResponse(
                    localModel.Provider,
                    localModel.BaseUrl,
                    localModel.DefaultModel,
                    apiKeyState.IsConfigured,
                    apiKeyState.Source,
                    apiKeyState.Fingerprint,
                    apiKeyState.UpdatedAt,
                    IsReachable: true,
                    ErrorMessage: null,
                    models));
            }
            catch (LocalModelException exception)
            {
                return Results.Ok(new LocalModelStatusResponse(
                    localModel.Provider,
                    localModel.BaseUrl,
                    localModel.DefaultModel,
                    apiKeyState.IsConfigured,
                    apiKeyState.Source,
                    apiKeyState.Fingerprint,
                    apiKeyState.UpdatedAt,
                    IsReachable: false,
                    ErrorMessage: exception.Message,
                    Models: []));
            }
        })
        .Produces<LocalModelStatusResponse>()
        .WithSummary("Get model runtime status")
        .WithDescription("Returns the configured model provider, base URL, default model, whether the runtime is reachable, and the models currently available to the runtime.")
        .WithName("GetLocalModels");

        localModels.MapPost("/openrouter-key", (
            SaveOpenRouterApiKeyRequest request,
            OpenRouterRuntimeSettings openRouterRuntimeSettings) =>
        {
            var apiKey = OpenRouterRuntimeSettings.SanitizeApiKey(request.ApiKey);

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Results.BadRequest(new { error = "ApiKey is required." });
            }

            if (!apiKey.StartsWith("sk-or-", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "This does not look like an OpenRouter key. It should usually start with sk-or-." });
            }

            openRouterRuntimeSettings.SetApiKey(apiKey);

            var snapshot = openRouterRuntimeSettings.GetSnapshot();

            return Results.Ok(new
            {
                isApiKeyConfigured = true,
                apiKeySource = "Runtime",
                apiKeyFingerprint = snapshot.Fingerprint,
                apiKeyUpdatedAt = snapshot.UpdatedAt
            });
        })
        .Accepts<SaveOpenRouterApiKeyRequest>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithSummary("Save OpenRouter key for this API run")
        .WithDescription("Stores an OpenRouter API key in backend memory only. The key is not written to appsettings, localStorage, or the database.")
        .WithName("SaveOpenRouterApiKey");

        localModels.MapDelete("/openrouter-key", (OpenRouterRuntimeSettings openRouterRuntimeSettings) =>
        {
            openRouterRuntimeSettings.ClearApiKey();
            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .WithSummary("Clear runtime OpenRouter key")
        .WithDescription("Clears the in-memory OpenRouter key for the current backend process.")
        .WithName("ClearOpenRouterApiKey");

        return app;
    }

    private static OpenRouterApiKeyState GetOpenRouterApiKeyState(OpenRouterRuntimeSettings runtimeSettings)
    {
        if (runtimeSettings.HasRuntimeApiKey)
        {
            var snapshot = runtimeSettings.GetSnapshot();
            return new OpenRouterApiKeyState(true, "Runtime", snapshot.Fingerprint, snapshot.UpdatedAt);
        }

        return new OpenRouterApiKeyState(false, "Missing", string.Empty, null);
    }

    private sealed record OpenRouterApiKeyState(
        bool IsConfigured,
        string Source,
        string Fingerprint,
        DateTimeOffset? UpdatedAt);
}
