using DeviceAuditor.Models;
using DeviceAuditor.Services.Interfaces;
using Microsoft.Win32;

namespace DeviceAuditor.Services
{
    /// <summary>
    /// Implements <see cref="IRepairService"/> 
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public class RepairService : IRepairService
    {
        /// <inheritdoc/>
        public bool FixDevice(DeviceSummary device)
        {
            if (string.IsNullOrWhiteSpace(device.RegistryPath) ||
                !device.RegistryPath.StartsWith(@"SYSTEM\CurrentControlSet\Enum\", StringComparison.OrdinalIgnoreCase) ||
                !device.RegistryPath.EndsWith(@"\Device Parameters", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  [!] Invalid registry path provided.");
                return false;
            }

            try
            {
                // CreateSubKey will create missing intermediates if needed (good)
                using var key = Registry.LocalMachine.CreateSubKey(device.RegistryPath, writable: true);
                if (key == null)
                {
                    Console.WriteLine("  [!] Failed to open/create registry key (possibly access denied).");
                    return false;
                }

                // Set to 0 = disable enhanced power management / selective suspend
                key.SetValue("EnhancedPowerManagementEnabled", 0, RegistryValueKind.DWord);

                // Optional: verify it stuck
                var readBack = key.GetValue("EnhancedPowerManagementEnabled");
                if (readBack is not int val || val != 0)
                {
                    Console.WriteLine("  [!] Value was set but verification failed.");
                    return false;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  [✓] EnhancedPowerManagementEnabled set to 0.");
                Console.ResetColor();
                Console.WriteLine("     → Unplug/replug the device or reboot for the change to take full effect.");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [!] Access Denied → This tool must be run as Administrator.");
                Console.ResetColor();
                return false;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [!] Failed to apply fix: {ex.GetType().Name} - {ex.Message}");
                Console.ResetColor();
                return false;
            }
        }
    }
}
