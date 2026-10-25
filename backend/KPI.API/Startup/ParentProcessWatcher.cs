using System.Diagnostics;

namespace KPI.API.Startup;

/// <summary>
/// The Windows app can start this program as its own built-in server and tells it which process started it (HELPDESK_PARENT_PID).
/// The server must not outlive the app: a server left running after the app was closed or killed would keep its files locked,
/// so the next update could not replace them. When that process is gone, this one exits too.
/// </summary>
public static class ParentProcessWatcher
{
    public const string Variable = "HELPDESK_PARENT_PID";

    /// <summary>Starts watching when the environment names a parent process; does nothing otherwise.</summary>
    public static void StartFromEnvironment(Func<string, string?> env, Action exit)
    {
        if (!int.TryParse(env(Variable), out var pid) || pid <= 0) return;
        _ = Watch(pid, TimeSpan.FromSeconds(2), exit, CancellationToken.None);
    }

    /// <summary>Calls <paramref name="onGone"/> once when the process <paramref name="pid"/> is no longer the one it was when watching began.</summary>
    public static Task Watch(int pid, TimeSpan interval, Action onGone, CancellationToken stop) => Task.Run(async () =>
    {
        // The start time tells the process from another one that was given the same number later.
        var started = StartTimeOf(pid);
        while (!stop.IsCancellationRequested)
        {
            if (started is null || StartTimeOf(pid) != started) { onGone(); return; }
            try { await Task.Delay(interval, stop); } catch (OperationCanceledException) { return; }
        }
    }, stop);

    private static DateTime? StartTimeOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : process.StartTime;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;   // no such process (any more), or not ours to look at
        }
    }
}
