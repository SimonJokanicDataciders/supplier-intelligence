namespace SupplierIntelligence.Api.Services;

public class LocalModelException(string message, Exception? innerException = null)
    : Exception(message, innerException);
