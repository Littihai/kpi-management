using System.Diagnostics;
using KPI.API.Startup;
using Xunit;

namespace KPI.Tests;

public class ParentProcessWatcherTests
{
    [Fact]
    public async Task A_parent_that_is_still_running_is_left_alone()
    {
        var gone = 0;
        using var stop = new CancellationTokenSource();

        var watching = ParentProcessWatcher.Watch(Environment.ProcessId, TimeSpan.FromMilliseconds(20), () => Interlocked.Increment(ref gone), stop.Token);
        await Task.Delay(300);
        await stop.CancelAsync();
        await watching;

        Assert.Equal(0, gone);
    }

    [Fact]
    public async Task A_parent_that_has_exited_is_noticed_once()
    {
        using var child = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
        var pid = child.Id;
        await child.WaitForExitAsync();
        var gone = 0;
        var noticed = new TaskCompletionSource();

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watching = ParentProcessWatcher.Watch(pid, TimeSpan.FromMilliseconds(20), () => { Interlocked.Increment(ref gone); noticed.TrySetResult(); }, stop.Token);

        await noticed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await watching;
        Assert.Equal(1, gone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a number")]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task Nothing_is_watched_unless_the_environment_names_a_parent(string? value)
    {
        var exited = false;

        ParentProcessWatcher.StartFromEnvironment(name => name == ParentProcessWatcher.Variable ? value : null, () => exited = true);
        await Task.Delay(100);

        Assert.False(exited);
    }
}
