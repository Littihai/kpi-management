namespace KPI.Application.Notifications;

/// <summary>One thing the connection check tried: <see cref="Name"/> says what, <see cref="Detail"/> what it found.</summary>
public record EmailDiagnosisStep(string Name, bool Ok, string Detail);

/// <summary>
/// What the connection check found. A step that fails stops the check, so the steps after it are absent.
/// <see cref="Advice"/> is plain Thai, concrete enough to act on or to hand to the network or mail administrator.
/// </summary>
public record EmailDiagnosis(
    bool Ok,
    string Host,
    int Port,
    string Security,
    IReadOnlyList<EmailDiagnosisStep> Steps,
    IReadOnlyList<string> Advice);

public interface IEmailDiagnostics
{
    /// <summary>
    /// Walks through the saved SMTP settings (name lookup, opening the port, the server's greeting, encryption) and says
    /// where it stops working. It never signs in and never sends a message, so it needs no password and cannot leak one.
    /// </summary>
    Task<EmailDiagnosis> DiagnoseAsync(CancellationToken cancellationToken = default);
}
