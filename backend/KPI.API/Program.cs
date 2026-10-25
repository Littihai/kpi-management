using KPI.API.Startup;
using KPI.API.Startup.Setup;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Started by the Windows app as its built-in server: leave when the app is gone (see ParentProcessWatcher).
ParentProcessWatcher.StartFromEnvironment(Environment.GetEnvironmentVariable, () => Environment.Exit(0));

// `KPI.API create-admin` creates the first administrator and exits (see AdminCommand).
if (args.Length > 0 && args[0].Equals(AdminCommand.Name, StringComparison.OrdinalIgnoreCase))
{
    var tool = Supervisor.NewBuilder(args);
    tool.Logging.SetMinimumLevel(LogLevel.Warning);   // the command prints a line or two; the framework's own chatter would drown them
    MainApp.ConfigureServices(tool);
    var toolApp = tool.Build();
    return await AdminCommand.RunAsync(toolApp.Services, Environment.GetEnvironmentVariable, Console.Out);
}

var builder = Supervisor.NewBuilder(args);

if (!SetupMode.UsesSupervisor(builder.Configuration, builder.Environment))
{
    // The tests, and a PostgreSQL deployment that is already configured: start the application directly, as always.
    builder.Host.UseWindowsService();
    var direct = await MainApp.BuildAsync(builder);
    direct.Run();
    return 0;
}

// SQL Server, or no database chosen yet: a supervisor starts the application when the database answers, and the setup
// page while it does not, inside one service so the service control manager never sees it stop.
var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices(services => services.AddHostedService(sp =>
        new Supervisor(args, sp.GetRequiredService<ILogger<Supervisor>>(), sp.GetRequiredService<IHostApplicationLifetime>())))
    .Build();
await host.RunAsync();
return 0;

// Lets the integration-test project start the app with WebApplicationFactory<Program>.
public partial class Program { }
