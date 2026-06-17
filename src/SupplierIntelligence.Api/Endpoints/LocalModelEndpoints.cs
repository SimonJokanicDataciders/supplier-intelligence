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
            CancellationToken cancellationToken) =>
        {
            try
            {
                var models = await localModel.GetModelsAsync(cancellationToken);

                return Results.Ok(new LocalModelStatusResponse(
                    localModel.Provider,
                    localModel.BaseUrl,
                    localModel.DefaultModel,
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
                    IsReachable: false,
                    ErrorMessage: exception.Message,
                    Models: []));
            }
        })
        .Produces<LocalModelStatusResponse>()
        .WithSummary("Get model runtime status")
        .WithDescription("Returns the configured model provider, base URL, default model, whether the runtime is reachable, and the models currently available to the runtime.")
        .WithName("GetLocalModels");

        return app;
    }
}
