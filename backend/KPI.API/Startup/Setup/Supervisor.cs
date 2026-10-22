using KPI.API.Startup;

namespace KPI.API.Startup.Setup;

/// <summary>
/// Keeps the application going and swaps what is behind the port: the setup page while the database is not configured or
/// not answering, the application once it is. It runs inside the one outer host (the Windows service), so the service control
/// manager sees a single, unbroken service while the web servers underneath are stopped and started.
/// </summary>
public sealed class Supervisor : BackgroundService
{
    private readonly string[] _args;
    private readonly ILogger<Supervisor> _log;
    private readonly IHostApplicationLifetime _lifetime;

    public Supervisor(string[] args, ILogger<Supervisor> log, IHostApplicationLifetime lifetime)
    {
        _args = args;
        _log = log;
        _lifetime = lifetime;
    }

    public static WebApplicationBuilder NewBuilder(string[] args)
    {
        // As a service the working folder is System32: the program's own folder is the content root.
        var options = new WebApplicationOptions { Args = args, ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null };
        var builder = WebApplication.CreateBuilder(options);
        DatabaseConfigFile.Add(builder.Configuration, builder.Configuration, builder.Environment.ContentRootPath);
        return builder;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var builder = NewBuilder(_args);
                var need = await SetupMode.CheckAsync(builder.Configuration, _log, stoppingToken);

                WebApplication? app = null;
                if (need is null)
                {
                    try { app = await MainApp.BuildAsync(builder); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        // e.g. the account may not change the schema: say so on the setup page instead of dying in a loop
                        _log.LogError(ex, "The application could not start");
                        need = new SetupNeed(SetupReason.StartupFailed, null, ex.GetBaseException().Message);
                        builder = NewBuilder(_args);
                    }
                }

                if (app is null)
                {
                    if (!await SetupHost.RunAsync(builder, need!, _log, stoppingToken)) break;   // asked to stop while waiting
                    continue;                                                                       // set up: start again, now for real
                }

                await RunAsync(app, stoppingToken);
                break;
            }
        }
        catch (OperationCanceledException) { /* stopping */ }
        finally
        {
            _lifetime.StopApplication();
        }
    }

    private static async Task RunAsync(WebApplication app, CancellationToken stoppingToken)
    {
        await app.StartAsync(stoppingToken);
        var stoppedByItself = new TaskCompletionSource();
        app.Lifetime.ApplicationStopped.Register(() => stoppedByItself.TrySetResult());
        try { await Task.WhenAny(stoppedByItself.Task, Task.Delay(Timeout.Infinite, stoppingToken)); }
        catch (OperationCanceledException) { /* stopping */ }
        await app.StopAsync(CancellationToken.None);
        await app.DisposeAsync();
    }
}
