using CommandLine;
using DeviceAuditor.Models;
using DeviceAuditor.Services;
using DeviceAuditor.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using System.Runtime.InteropServices;

class PeripheralAuditor
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);

    static void Main(string[] args)
    {
        // Fix for single-file P/Invoke to system DLLs
        SetDllDirectory("");

        // ── Configure Serilog ───────────────────────────────────────────────
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()                           // Capture everything in files
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                theme: AnsiConsoleTheme.Code,
                restrictedToMinimumLevel: LogEventLevel.Information  // Console quieter than file
            )
            .WriteTo.File(
                path: "logs/audit-.log",                        // → creates audit-20260218.log etc.
                rollingInterval: RollingInterval.Day,           // new file per day
                retainedFileCountLimit: 31,                     // keep last 31 days (optional but recommended)
                fileSizeLimitBytes: 50 * 1024 * 1024,           // ~50 MB per file (optional safety)
                rollOnFileSizeLimit: true,                      // roll early if file gets too big
                buffered: true,                                 // better perf, flush on dispose
                flushToDiskInterval: TimeSpan.FromSeconds(1)    // ensure timely writes
            )
            .CreateLogger();

        try
        {
            Log.Debug("Starting Peripheral Auditor (CTS v3.x)");  // Use Info instead of Debug for startup

            var services = new ServiceCollection();

            services.AddSingleton<IDeviceDatabase, DeviceDatabase>();
            services.AddSingleton<IAuditService, AuditService>();
            services.AddSingleton<IRepairService, RepairService>();

            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddSerilog(dispose: true);
            });

            var serviceProvider = services.BuildServiceProvider();

            var logger = serviceProvider.GetRequiredService<ILogger<PeripheralAuditor>>();
            logger.LogDebug("Dependency injection container built successfully");

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(opts =>
                {
                    var auditor = serviceProvider.GetRequiredService<IAuditService>();
                    auditor.Run(opts);
                })
                .WithNotParsed(errors =>
                {
                    var realErrors = errors
                        .Where(e => e.Tag != ErrorType.VersionRequestedError && e.Tag != ErrorType.HelpRequestedError)
                        .ToList();

                    if (realErrors.Count != 0)
                    {
                        Log.Warning("Command-line parsing failed with real errors");
                        foreach (var error in realErrors)
                        {
                            Log.Error("Parse error: {Error}", error.Tag);
                        }
                        Environment.ExitCode = 1;
                    }
                    // --help / --version are handled automatically by the parser → silent success here
                });
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
            Environment.ExitCode = 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}