namespace SupplierIntelligence.Api.Services;

public sealed class RuleBasedCertificationFallbackConnector : ICertificationVerificationConnector
{
    public string Name => "Local rule-based fallback";

    public bool CanVerify(CertificationVerificationInput input)
    {
        return true;
    }

    public Task<CertificationVerificationResult?> TryVerifyAsync(
        CertificationVerificationInput input,
        CancellationToken cancellationToken)
    {
        var standard = input.Standard.Trim();
        var certificateNumber = input.CertificateNumber.Trim();
        var issuer = input.Issuer.Trim();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        if (input.ValidUntil is null)
        {
            return Task.FromResult<CertificationVerificationResult?>(new CertificationVerificationResult(
                false,
                "Local fallback could not confirm the certificate because no validity date was provided."));
        }

        if (input.ValidUntil < today)
        {
            return Task.FromResult<CertificationVerificationResult?>(new CertificationVerificationResult(
                false,
                $"Local fallback marked this unverified because the certificate expired on {input.ValidUntil:yyyy-MM-dd}."));
        }

        if (certificateNumber.Length < 5 || issuer.Length < 3 || standard.Length < 3)
        {
            return Task.FromResult<CertificationVerificationResult?>(new CertificationVerificationResult(
                false,
                "Local fallback needs a standard, issuer, and certificate number before it can trust the certificate."));
        }

        return Task.FromResult<CertificationVerificationResult?>(new CertificationVerificationResult(
            true,
            "Local fallback accepted the certificate fields and future validity date. Registry/PDF verification can be plugged in through connector interfaces."));
    }
}
