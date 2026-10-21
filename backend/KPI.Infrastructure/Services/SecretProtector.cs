using System.Security.Cryptography;
using System.Text;
using KPI.Application.Settings;

namespace KPI.Infrastructure.Services;

/// <summary>
/// AES-GCM with a key derived from the server's JWT signing key, so no extra secret has to be managed (and nothing is
/// lost when the host restarts, unlike per-instance data-protection keys). Rotating the JWT key makes stored secrets
/// unreadable; the administrator then enters them again.
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    private const string Prefix = "enc1:";
    private readonly byte[] _key;

    public SecretProtector(string signingKey)
    {
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(signingKey), 32, salt: null,
            info: Encoding.UTF8.GetBytes("helpdesk-settings-secrets-v1"));
    }

    public string Protect(string plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, data, cipher, tag);
        return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string? Unprotect(string protectedValue)
    {
        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        try
        {
            var all = Convert.FromBase64String(protectedValue[Prefix.Length..]);
            if (all.Length < 28) return null;
            var nonce = all[..12];
            var tag = all[12..28];
            var cipher = all[28..];
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
