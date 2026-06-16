namespace SupplierIntelligence.Api.Services;

public interface ICertificationVerifier
{
    Task<CertificationVerificationResult> VerifyAsync(
        CertificationVerificationInput input,
        CancellationToken cancellationToken);
}

public interface ICertificationVerificationConnector
{
    string Name { get; }
    bool CanVerify(CertificationVerificationInput input);
    Task<CertificationVerificationResult?> TryVerifyAsync(
        CertificationVerificationInput input,
        CancellationToken cancellationToken);
}

public sealed record CertificationVerificationInput(
    string Standard,
    string CertificateNumber,
    string Issuer,
    DateOnly? ValidUntil);

public sealed record CertificationVerificationResult(
    bool IsVerified,
    string Notes);
