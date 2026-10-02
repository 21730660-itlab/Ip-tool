using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IpMonitor.Core;

/// <summary>Accounts, compatible with the web version: PBKDF2-SHA256, hex salt and hash, same pre-shared key.</summary>
public static class Auth
{
    /// <summary>SHA-256 of the pre-shared key needed to create an account (same key as the web version).</summary>
    public const string PskSha256 = "8b3ce0c3977ee6e8d53efeb1fb5b4f82bfb85e44b706c4eded197bd78875da67";
    public static readonly string[] Perms = { "read", "write", "full" };

    public static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
    public static string UserKey(string name) => (name ?? "").Trim().ToLowerInvariant();

    public static string Pbkdf2(string password, string saltHex, int iterations)
    {
        var pw = Encoding.UTF8.GetBytes(password ?? ""); var salt = Convert.FromHexString(saltHex);
        try { return Hex(Rfc2898DeriveBytes.Pbkdf2(pw, salt, iterations, HashAlgorithmName.SHA256, 32)); }
        catch (CryptographicException) { return Hex(Pbkdf2Managed(pw, salt, iterations)); }
    }

    /// <summary>PBKDF2-HMAC-SHA256, one 32-byte block, for systems where the built-in call is not available.</summary>
    public static byte[] Pbkdf2Managed(byte[] password, byte[] salt, int iterations)
    {
        using var h = new HMACSHA256(password);
        var u = h.ComputeHash(salt.Concat(new byte[] { 0, 0, 0, 1 }).ToArray());
        var t = (byte[])u.Clone();
        for (int i = 1; i < iterations; i++)
        {
            u = h.ComputeHash(u);
            for (int j = 0; j < t.Length; j++) t[j] ^= u[j];
        }
        return t;
    }

    public static bool CheckPsk(string psk) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hex(SHA256.HashData(Encoding.UTF8.GetBytes(psk ?? "")))), Encoding.ASCII.GetBytes(PskSha256));

    public static bool Verify(User u, string password)
    {
        if (u == null || string.IsNullOrEmpty(u.Salt) || string.IsNullOrEmpty(u.Hash)) return false;
        var h = Pbkdf2(password, u.Salt, u.Iter > 0 ? u.Iter : 150000);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h), Encoding.ASCII.GetBytes(u.Hash));
    }

    /// <summary>Checks the sign-up rules; returns an error message or null.</summary>
    public static string SignupProblem(string username, string password, string password2)
    {
        if (!Regex.IsMatch(UserKey(username), "^[a-z0-9._-]{3,32}$")) return "Username must be 3–32 characters: letters, numbers, dot, dash or underscore.";
        if ((password ?? "").Length < 8) return "Password must be at least 8 characters.";
        if (password != password2) return "Passwords don't match.";
        return null;
    }

    public static User NewUser(string username, string password, string role)
    {
        var salt = Hex(RandomNumberGenerator.GetBytes(16));
        const int iter = 150000;
        return new User
        {
            Id = UserKey(username), Username = username.Trim(), Role = role == "admin" ? "admin" : "guest",
            Perm = role == "admin" ? "full" : "read", Salt = salt, Iter = iter, Hash = Pbkdf2(password, salt, iter), CreatedAt = Entity.Now()
        };
    }

    public static void SetPassword(User u, string password)
    {
        u.Salt = Hex(RandomNumberGenerator.GetBytes(16)); u.Iter = 150000; u.Hash = Pbkdf2(password, u.Salt, u.Iter); u.UpdatedAt = Entity.Now();
    }
}
