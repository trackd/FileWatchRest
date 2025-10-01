namespace FileWatchRest.Configuration;

/// <summary>
/// Handles secure encryption and decryption of sensitive configuration data
/// using machine-specific encryption that only this application can access
/// </summary>
public static class SecureConfigurationHelper
{
    private static readonly byte[] AdditionalEntropy = Encoding.UTF8.GetBytes("FileWatchRest-SecureConfig-2025");
    private const string EncryptedTokenPrefix = "enc:";

    /// <summary>
    /// Encrypts a plain text bearer token for secure storage
    /// </summary>
    /// <param name="plainTextToken">The plain text bearer token</param>
    /// <returns>Encrypted token with "enc:" prefix</returns>
    public static string EncryptBearerToken(string plainTextToken)
    {
        if (string.IsNullOrWhiteSpace(plainTextToken))
            return string.Empty;

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Token encryption is only supported on Windows");

        try
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainTextToken);
            var encryptedBytes = ProtectedData.Protect(plainTextBytes, AdditionalEntropy, DataProtectionScope.LocalMachine);
            return EncryptedTokenPrefix + Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to encrypt bearer token", ex);
        }
    }

    /// <summary>
    /// Decrypts an encrypted bearer token for use
    /// </summary>
    /// <param name="encryptedToken">Encrypted token with "enc:" prefix</param>
    /// <returns>Plain text bearer token</returns>
    public static string DecryptBearerToken(string encryptedToken)
    {
        if (string.IsNullOrWhiteSpace(encryptedToken))
            return string.Empty;

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Token decryption is only supported on Windows");

        if (!encryptedToken.StartsWith(EncryptedTokenPrefix))
            throw new InvalidOperationException("Token does not have the expected encryption prefix");

        try
        {
            var base64Token = encryptedToken.Substring(EncryptedTokenPrefix.Length);
            var encryptedBytes = Convert.FromBase64String(base64Token);
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, AdditionalEntropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to decrypt bearer token. This may occur if the token was encrypted on a different machine or by a different user.", ex);
        }
    }

    /// <summary>
    /// Checks if a token is encrypted (has "enc:" prefix)
    /// </summary>
    /// <param name="token">Token to check</param>
    /// <returns>True if the token is encrypted</returns>
    public static bool IsTokenEncrypted(string? token)
    {
        return !string.IsNullOrWhiteSpace(token) && token.StartsWith(EncryptedTokenPrefix);
    }

    /// <summary>
    /// Migrates a plain text token to encrypted format if needed
    /// </summary>
    /// <param name="token">Token to potentially encrypt</param>
    /// <returns>Encrypted token if it was plain text, otherwise returns the token unchanged</returns>
    public static string EnsureTokenIsEncrypted(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        if (!OperatingSystem.IsWindows())
            return token; // Return unchanged on non-Windows platforms

        if (IsTokenEncrypted(token))
            return token; // Already encrypted

        // Encrypt the plain text token
        return EncryptBearerToken(token);
    }
}
