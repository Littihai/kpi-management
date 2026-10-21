namespace KPI.Application.Settings;

/// <summary>Encrypts values that must be stored but never shown again (an SMTP password).</summary>
public interface ISecretProtector
{
    string Protect(string plain);

    /// <summary>Returns null when the value was not written by this server's key (tampered, or the key changed).</summary>
    string? Unprotect(string protectedValue);
}
