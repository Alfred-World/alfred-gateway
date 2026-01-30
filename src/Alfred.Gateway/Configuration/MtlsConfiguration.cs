using System.Security.Cryptography.X509Certificates;

namespace Alfred.Gateway.Configuration;

/// <summary>
/// Configuration for mTLS (mutual TLS) client certificate.
/// Gateway uses this certificate when calling backend services.
/// </summary>
public class MtlsConfiguration
{
    /// <summary>
    /// Whether mTLS is enabled for outgoing requests to backend services
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// Path to the client certificate (PFX file) used when calling backend services
    /// </summary>
    public string? ClientCertPath { get; }

    /// <summary>
    /// Password for the client certificate PFX file
    /// </summary>
    public string? ClientCertPassword { get; }

    /// <summary>
    /// Path to the CA certificate for validating backend server certificates
    /// </summary>
    public string? CaCertPath { get; }

    /// <summary>
    /// Whether to skip server certificate validation (NOT recommended for production)
    /// </summary>
    public bool SkipServerCertValidation { get; }

    public MtlsConfiguration()
    {
        Enabled = GetBool("MTLS_ENABLED", false);
        ClientCertPath = GetOptional("MTLS_CLIENT_CERT_PATH");
        ClientCertPassword = GetOptional("MTLS_CLIENT_CERT_PASSWORD") ?? "";
        CaCertPath = GetOptional("MTLS_CA_CERT_PATH");
        SkipServerCertValidation = GetBool("MTLS_SKIP_SERVER_CERT_VALIDATION", false);

        if (Enabled)
        {
            ValidateConfiguration();
        }
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(ClientCertPath))
        {
            throw new InvalidOperationException(
                "MTLS_CLIENT_CERT_PATH is required when MTLS_ENABLED=true");
        }

        if (!File.Exists(ClientCertPath))
        {
            throw new InvalidOperationException(
                $"Client certificate not found at: {ClientCertPath}");
        }

        if (string.IsNullOrWhiteSpace(CaCertPath))
        {
            throw new InvalidOperationException(
                "MTLS_CA_CERT_PATH is required when MTLS_ENABLED=true");
        }

        if (!File.Exists(CaCertPath))
        {
            throw new InvalidOperationException(
                $"CA certificate not found at: {CaCertPath}");
        }
    }

    /// <summary>
    /// Load the client certificate from the PFX file
    /// </summary>
    public X509Certificate2 LoadClientCertificate()
    {
        if (string.IsNullOrWhiteSpace(ClientCertPath))
        {
            throw new InvalidOperationException("Client certificate path is not configured");
        }

        return X509CertificateLoader.LoadPkcs12FromFile(
            ClientCertPath,
            ClientCertPassword,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
    }

    /// <summary>
    /// Load the CA certificate for validating server certificates
    /// </summary>
    public X509Certificate2 LoadCaCertificate()
    {
        if (string.IsNullOrWhiteSpace(CaCertPath))
        {
            throw new InvalidOperationException("CA certificate path is not configured");
        }

        return X509CertificateLoader.LoadCertificateFromFile(CaCertPath);
    }

    private static string GetOptional(string key)
    {
        return Environment.GetEnvironmentVariable(key) ?? string.Empty;
    }

    private static bool GetBool(string key, bool defaultValue)
    {
        var value = GetOptional(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }
}
