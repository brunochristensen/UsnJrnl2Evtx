using UsnJrnl2Evtx;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using System.Security.Principal;
using System.IO;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

String appDir = AppContext.BaseDirectory;
builder.Configuration
    .AddJsonFile(Path.Combine(appDir, "appsettings.json"), optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(appDir, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: true);

builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

const string serviceName = "UsnJrnl2Evtx";
const string serviceDisplayName = "UsnJrnl2Evtx Logging Service";
const string serviceDescription = "Generates custom Windows Event Log entries.";
const string eventLogName = "UsnJrnl2Evtx"; //%SystemRoot%\\System32\\winevt\\Logs\\UsnJrnl2Evtx.evtx
const string eventSourceName = "UsnJrnl2Evtx";

if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
{
    EnsureAdminOrThrow();
    EventLogSetup.EnsureEventSource(eventSourceName, eventLogName);
    ServiceInstaller.Install(serviceName, serviceDisplayName, serviceDescription);
    Console.WriteLine($"Service '{serviceName}' installed and Event Log '{eventLogName}' ensured.");
    return;
}
if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
{
    EnsureAdminOrThrow();
    ServiceInstaller.Uninstall(serviceName);
    Console.WriteLine($"Service '{serviceName}' uninstalled.");
    return;
}

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = serviceDisplayName;
});

LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(builder.Services);
builder.Logging.ClearProviders().AddEventLog(settings =>
{
    settings.SourceName = eventSourceName;
    settings.LogName = eventLogName;
});

try
{
    EventLogSetup.EnsureEventSource(eventSourceName, eventLogName);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Warning: could not ensure event source '{eventSourceName}' in log '{eventLogName}': {ex.Message}");
}

builder.Services.AddHostedService<UsnLoggingService>();

IHost host = builder.Build();
host.Run();

static void EnsureAdminOrThrow()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        throw new InvalidOperationException("Administrator privileges are required for this action. Run an elevated terminal.");
}