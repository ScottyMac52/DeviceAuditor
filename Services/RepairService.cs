using DeviceAuditor.Models;
using DeviceAuditor.Services.Interfaces;
using Microsoft.Win32;

namespace DeviceAuditor.Services
{
    /// <summary>
    /// Implements <see cref="IRepairService"/> 
    /// </summary>
    public class RepairService : IRepairService
    {
        /// <inheritdoc/>
        public bool FixDevice(DeviceSummary device)
        {
            if (string.IsNullOrEmpty(device.RegistryPath)) return false;

            try
            {
                // Requires Administrative privileges to write to HKLM
                using (var key = Registry.LocalMachine.OpenSubKey(device.RegistryPath, true))
                {
                    if (key == null) return false;
                    key.SetValue("EnhancedPowerManagementEnabled", 0, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  [!] Error: Access Denied. Run as Administrator to apply fixes.");
                Console.ResetColor();
                return false;
            }
            catch { return false; }
        }
    }
}
