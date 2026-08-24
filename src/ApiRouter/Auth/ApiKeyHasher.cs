using System.Security.Cryptography;
using System.Text;

namespace ApiRouter.Auth;

/// <summary>
/// Generates and hashes API keys. Keys are high-entropy random tokens, so a fast
/// cryptographic hash (SHA-256) is the right tool — it lets us look a key up by its
/// hash without ever storing the secret, while brute-forcing the 256-bit space is
/// infeasible. (Slow password hashes like bcrypt are for low-entropy human passwords
/// and can't be looked up by value.)
/// </summary>
public static class ApiKeyHasher
{
    /// <summary>Creates a new random API key (64 hex characters, 256 bits of entropy).</summary>
    public static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Returns the lowercase hex SHA-256 hash used to store and look up a key.</summary>
    public static string Hash(string apiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
