using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SupplierIntelligence.Api.Data;
using SupplierIntelligence.Api.Endpoints;
using SupplierIntelligence.Api.Options;
using SupplierIntelligence.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("AppDb")));

builder.Services.Configure<LocalModelOptions>(
    builder.Configuration.GetSection("LocalModel"));

var localModelOptions = builder.Configuration
    .GetSection("LocalModel")
    .Get<LocalModelOptions>() ?? new LocalModelOptions();

if (localModelOptions.Provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<ILocalModelClient, OpenRouterLocalModelClient>((services, client) =>
    {
        var options = services.GetRequiredService<IOptions<LocalModelOptions>>().Value;
        client.BaseAddress = new Uri(options.OpenRouterBaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    });
}
else
{
    builder.Services.AddHttpClient<ILocalModelClient, OllamaLocalModelClient>((services, client) =>
    {
        var options = services.GetRequiredService<IOptions<LocalModelOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    });
}

builder.Services.AddHttpClient<ISourceEvidenceChecker, HttpSourceEvidenceChecker>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddHttpClient<IWebsiteCertificationDiscovery, HttpWebsiteCertificationDiscovery>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddHttpClient<IWebsiteResearcher, HttpWebsiteResearcher>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
});
builder.Services.AddSingleton<ISourceDiscoveryPlanner, SourceDiscoveryPlanner>();
builder.Services.AddSingleton<ISupplierResearchQueryPlanner, SupplierResearchQueryPlanner>();
builder.Services.AddSingleton<IResearchFactExtractor, ResearchFactExtractor>();
builder.Services.AddSingleton<ICertificationVerificationConnector, LocalLearningRegistryCertificationConnector>();
builder.Services.AddSingleton<ICertificationVerificationConnector, RuleBasedCertificationFallbackConnector>();
builder.Services.AddSingleton<ICertificationVerifier, CompositeCertificationVerifier>();
builder.Services.AddSingleton<ISupplierAnalysisQueue, ChannelSupplierAnalysisQueue>();
builder.Services.AddHostedService<AdaptiveSupplierAnalysisWorker>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddOpenApi();

var app = builder.Build();

await SqliteSchemaInitializer.EnsureResearchSchemaAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapDevelopmentDataEndpoints();
}

app.MapLocalModelEndpoints();
app.MapSupplierEndpoints();

app.Run();
