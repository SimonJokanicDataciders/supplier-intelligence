namespace SupplierIntelligence.Api.Services;

public sealed class CompositeCertificationVerifier(
    IEnumerable<ICertificationVerificationConnector> connectors) : ICertificationVerifier
{
    public async Task<CertificationVerificationResult> VerifyAsync(
        CertificationVerificationInput input,
        CancellationToken cancellationToken)
    {
        foreach (var connector in connectors)
        {
            if (!connector.CanVerify(input))
            {
                continue;
            }

            var result = await connector.TryVerifyAsync(input, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        return new CertificationVerificationResult(
            false,
            "No certification verification connector could evaluate this certificate.");
    }
}
