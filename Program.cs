using CommandLine;
using DeviceAuditor.Models;
using DeviceAuditor.Services;
using DeviceAuditor.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

class PeripheralAuditor
{
    // --- MAIN ENTRY POINT ---
    static class Program
    {
        static void Main(string[] args)
        {
            var serviceProvider = new ServiceCollection()
                .AddSingleton<IDeviceDatabase, DeviceDatabase>()
                .AddSingleton<IAuditService, AuditService>()
                .AddSingleton<IRepairService, RepairService>()
                .BuildServiceProvider();

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(opts =>
                {
                    var auditor = serviceProvider.GetRequiredService<IAuditService>();
                    auditor.Run(opts);
                });
        }
    }
}