namespace SupplierIntelligence.Api.Services;

public interface IWebsiteCertificationDiscovery
{
    Task<IReadOnlyList<DiscoveredCertification>> DiscoverAsync(
        Uri websiteUrl,
        CancellationToken cancellationToken);
}

public sealed record DiscoveredCertification(
    string Standard,
    string CertificateNumber,
    string Issuer,
    DateOnly? ValidUntil,
    string Notes);
