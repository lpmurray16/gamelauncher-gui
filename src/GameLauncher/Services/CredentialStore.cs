using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GameLauncher.Services;

/// <summary>
/// Per-user local key storage using Windows DPAPI (CurrentUser scope).
/// Keys encrypted with this scope are only decryptable by the same Windows user
/// on this machine. Stored ciphertext files are readable only by this account.
/// </summary>
public sealed class CredentialStore(AppPaths paths)
{
    public const string SteamGridDb = "SteamGridDB";
    private static readonly string[] Providers = [SteamGridDb];

    private static string FileNameFor(string provider) => $"key.{provider}.dat";

    private string PathFor(string provider)
    {
        if (!Providers.Contains(provider)) throw new ArgumentException("Unknown provider.");
        return Path.Combine(paths.DataDirectory, FileNameFor(provider));
    }

    public bool HasKey(string provider)
    {
        if (!File.Exists(PathFor(provider))) return false;
        var key = Read(provider);
        return !string.IsNullOrEmpty(key);
    }

    public void Save(string provider, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length < 8 || key.Trim().Length > 200)
            throw new ArgumentException("That does not look like a valid API key.");
        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser);
        var temporary = PathFor(provider) + ".tmp";
        File.WriteAllBytes(temporary, cipher);
        File.Move(temporary, PathFor(provider), overwrite: true);
    }

    public string? Read(string provider)
    {
        var path = PathFor(provider);
        if (!File.Exists(path)) return null;
        try
        {
            var cipher = File.ReadAllBytes(path);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            // User/machine changed or file corrupted; treat as missing rather than crashing.
            return null;
        }
    }

    public void Delete(string provider) => File.Delete(PathFor(provider));
}
