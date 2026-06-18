namespace SupplierIntelligence.Api.Services;

public sealed class OpenRouterRuntimeSettings
{
    private readonly object gate = new();
    private string? apiKey;
    private DateTimeOffset? updatedAt;

    public bool HasRuntimeApiKey
    {
        get
        {
            lock (gate)
            {
                return !string.IsNullOrWhiteSpace(apiKey);
            }
        }
    }

    public void SetApiKey(string value)
    {
        lock (gate)
        {
            apiKey = SanitizeApiKey(value);
            updatedAt = DateTimeOffset.UtcNow;
        }
    }

    public OpenRouterApiKeySnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new OpenRouterApiKeySnapshot(
                apiKey,
                BuildFingerprint(apiKey),
                updatedAt);
        }
    }

    public void ClearApiKey()
    {
        lock (gate)
        {
            apiKey = null;
            updatedAt = null;
        }
    }

    public static string SanitizeApiKey(string value)
    {
        var compactValue = new string(value
            .Where(character => !char.IsWhiteSpace(character) && character != '\0')
            .ToArray());

        var keyStart = compactValue.IndexOf("sk-or-", StringComparison.OrdinalIgnoreCase);

        if (keyStart >= 0)
        {
            compactValue = compactValue[keyStart..];
        }

        return compactValue.Trim('"', '\'', '`', ';', ',', '.');
    }

    public static string BuildFingerprint(string? value)
    {
        var sanitized = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : SanitizeApiKey(value);

        if (sanitized.Length < 10)
        {
            return string.Empty;
        }

        return $"{sanitized[..5]}...{sanitized[^4..]}";
    }
}

public sealed record OpenRouterApiKeySnapshot(
    string? ApiKey,
    string Fingerprint,
    DateTimeOffset? UpdatedAt);
