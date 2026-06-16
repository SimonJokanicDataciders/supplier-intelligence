namespace SupplierIntelligence.Api.Services;

public sealed class LocalLearningRegistryCertificationConnector : ICertificationVerificationConnector
{
    private static readonly string[] TrustedPrefixes =
    [
        "LEARN-",
        "AUTO-",
        "STEP39-"
    ];

    public string Name => "Local learning registry";

    public bool CanVerify(CertificationVerificationInput input)
    {
        return input.Issuer.Contains("Learning", StringComparison.OrdinalIgnoreCase) ||
            TrustedPrefixes.Any(prefix =>
                input.CertificateNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public Task<CertificationVerificationResult?> TryVerifyAsync(
        CertificationVerificationInput input,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        if (input.ValidUntil is null)
        {
            return Task.FromResult<CertificationVerificationResult?>(new CertificationVerificationResult(
                false,
                "Local learning registry found the certificate pattern but could not verify it without a validity date."));
        }

        if (input.ValidUntil < today)
        {
            return Task.FromResult<CertificationVerificationResult?>(new CertificationVerificationResult(
                false,
                $"Local learning registry found the certificate pattern, but it expired on {input.ValidUntil:yyyy-MM-dd}."));
        }

        return Task.FromResult<CertificationVerificationResult?>(new CertificationVerificationResult(
            true,
            $"Verified by {Name}. This connector represents where a real certificate registry or PDF extraction connector plugs into the app."));
    }
}
