using DeviceAuditor.Models;
using DeviceAuditor.Services.Interfaces;
using Microsoft.Win32;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace DeviceAuditor.Services
{
    /// <summary>
    /// Implements <see cref="IAuditService"/> 
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Windows Only>")]
    public class AuditService : IAuditService
    {
        private const int CM_LOCATE_DEVNODE_NORMAL = 0x00000000;
        private const int CM_LOCATE_DEVNODE_PHANTOM = 0x00000001;
        private const int CR_SUCCESS = 0x00000000;

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Auto)]
        private static extern int CM_Locate_DevNode(out uint dnDevInst, string pszDeviceID, int ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Auto)]
        private static extern int CM_Get_Parent(out uint dnParentInst, uint dnDevInst, int ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Auto)]
        private static extern int CM_Get_Device_ID(uint dnDevInst, StringBuilder buffer, int bufferLen, int ulFlags);

        private readonly IDeviceDatabase _db;
        private readonly IRepairService _repair;

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="db"></param>
        /// <param name="repair"></param>
        public AuditService(IDeviceDatabase db, IRepairService repair)
        {
            _db = db;
            _repair = repair;
        }

        /// <inheritdoc/>
        public void Run(Options opts)
        {
            Console.WriteLine("===========================================================");
            Console.WriteLine(" MULTI-VENDOR PERIPHERAL AUDITOR - CTS v3.x ENGINE");
            Console.WriteLine("===========================================================\n");

            if (!_db.Load()) { Console.WriteLine("Error: devices.json missing."); return; }

            string[] vendorList = opts.Vendors.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var activeList = new List<DeviceSummary>();
            var inactiveList = new List<DeviceSummary>();

            foreach (var vid in vendorList)
            {
                string cleanVid = vid.Trim().ToUpper();

                // 1. ACTIVE SCAN (WMI) - Fix this loop!
                var searcher = new ManagementObjectSearcher($@"SELECT DeviceID, Caption FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 0 AND DeviceID LIKE 'HID\\VID_{cleanVid}%'");
                foreach (ManagementObject node in searcher.Get())
                {
                    if (!(node["Caption"]?.ToString()?.Contains("game controller", StringComparison.OrdinalIgnoreCase) ?? false)) continue;

                    // Use a locally defined ID variable from the node
                    string? fullId = node["DeviceID"]?.ToString();
                    if (string.IsNullOrEmpty(fullId)) continue;

                    string? pid = ExtractPid(fullId);
                    string? root = ExtractPhysicalRoot(fullId);

                    // CRITICAL: Pass 'fullId', NOT 'cleanVid'
                    var pwrInfo = GetParentPowerInfo(fullId);

                    if (!(activeList?.Any(x => x.InstanceID == root) ?? false))
                        activeList?.Add(new DeviceSummary { Name = _db.GetName(pid, cleanVid), InstanceID = root, Status = pwrInfo.Status, RegistryPath = pwrInfo.Path });
                }

                // 2. INACTIVE SCAN (REGISTRY)
                if (!opts.ActiveOnly)
                {
                    // 1. Project the IDs safely, filtering out any potential nulls
                    var activeIds = activeList?
                        .Select(a => a.InstanceID ?? string.Empty)
                        .ToList() ?? [];

                    // 2. Call the method and ensure we don't pass a null list to AddRange
                    var ghosts = GetGhostsFromRegistry(cleanVid, activeIds);
                    if (ghosts != null)
                    {
                        inactiveList.AddRange(ghosts);
                    }
                }
            }

            ProcessCategory("ACTIVE HID GAME CONTROLLERS", activeList, opts.Fix);
            if (!opts.ActiveOnly) ProcessCategory("INACTIVE / HIDDEN CONTROLLERS", inactiveList, opts.Fix);

            Console.WriteLine("\nScan Complete. Press any key to exit.");
            if (!Console.IsInputRedirected) Console.ReadKey();
        }

        private void ProcessCategory(string? title, List<DeviceSummary>? devices, bool autoFix)
        {
            Console.WriteLine($"--- {title} ---");
            if (!(devices?.Any() ?? false)) { Console.WriteLine("None detected."); return; }

            foreach (var dev in devices)
            {
                PrintDeviceLine(dev);
                if (autoFix && dev.Status != 0 && dev.Status != -1)
                {
                    bool success = _repair.FixDevice(dev);
                    if (success) { Console.WriteLine($"      -> Applied FIX: EnhancedPowerManagementEnabled set to 0."); }
                }
            }
            Console.WriteLine();
        }

        private List<DeviceSummary> GetGhostsFromRegistry(string vid, List<string> activeIds)
        {
            var ghosts = new List<DeviceSummary>();
            using (var hidKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\HID"))
            {
                if (hidKey == null) return ghosts;
                foreach (var pidKeyName in hidKey.GetSubKeyNames().Where(x => x.Contains($"VID_{vid}")))
                {
                    using (var pidKey = hidKey.OpenSubKey(pidKeyName))
                    {
                        foreach (var instanceName in pidKey?.GetSubKeyNames() ?? [])
                        {
                            var fullHidId = $@"HID\{pidKeyName}\{instanceName}";
                            var rootId = ExtractPhysicalRootFromInstance(instanceName);

                            if (!activeIds.Contains(rootId, StringComparer.OrdinalIgnoreCase))
                            {
                                var pid = ExtractPid(pidKeyName);
                                var pwrInfo = GetParentPowerInfo(fullHidId);

                                if (!ghosts.Any(x => x?.InstanceID?.Equals(rootId, StringComparison.OrdinalIgnoreCase) ?? false))
                                    ghosts.Add(new DeviceSummary { Name = _db.GetName(pid, vid), InstanceID = rootId, Status = pwrInfo.Status, RegistryPath = pwrInfo.Path });
                            }
                        }
                    }
                }
            }
            return ghosts;
        }

        private static (int Status, string? Path) GetParentPowerInfo(string hidInstanceId)
        {
            if (string.IsNullOrWhiteSpace(hidInstanceId)) return (-1, null);

            // Try to locate the device node (normal first for active devices, then phantom for ghosts)
            uint dnDevInst;
            int flags = CM_LOCATE_DEVNODE_NORMAL;
            int cr = CM_Locate_DevNode(out dnDevInst, hidInstanceId, flags);
            if (cr != CR_SUCCESS)
            {
                flags = CM_LOCATE_DEVNODE_PHANTOM;
                cr = CM_Locate_DevNode(out dnDevInst, hidInstanceId, flags);
                if (cr != CR_SUCCESS)
                {
                    return (-1, null); // Failed to locate device
                }
            }

            // Get the parent device instance
            uint parentInst;
            cr = CM_Get_Parent(out parentInst, dnDevInst, 0);
            if (cr != CR_SUCCESS)
            {
                return (-1, null); // No parent or error
            }

            // Get the parent device ID
            StringBuilder buffer = new StringBuilder(1024);
            cr = CM_Get_Device_ID(parentInst, buffer, buffer.Capacity, 0);
            if (cr != CR_SUCCESS)
            {
                return (-1, null); // Failed to get ID
            }

            string parentId = buffer.ToString();
            string fullPath = $@"SYSTEM\CurrentControlSet\Enum\{parentId}\Device Parameters";

            // Read the power management value
            using (var pKey = Registry.LocalMachine.OpenSubKey(fullPath, false))
            {
                if (pKey != null)
                {
                    var val = pKey.GetValue("EnhancedPowerManagementEnabled");
                    return (val != null ? Convert.ToInt32(val) : -1, fullPath);
                }
            }

            // Path exists but key is missing, or path doesn't exist yet
            return (-1, fullPath);
        }

        private static void PrintDeviceLine(DeviceSummary s)
        {
            Console.Write($"{s.Name,-38} | ID: {s.InstanceID,-15} | PWR: ");
            if (s.Status == 0) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("OPTIMAL"); }
            else if (s.Status == 1) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("UNSTABLE"); }
            else Console.WriteLine("DEFAULT");
            Console.ResetColor();
        }

        private static string? ExtractPhysicalRoot(string? fullId) => ExtractPhysicalRootFromInstance(fullId?.Split('\\')?.Last());
        private static string? ExtractPhysicalRootFromInstance(string? instancePart)
        {
            int secondAmp = instancePart?.IndexOf('&', instancePart.IndexOf('&') + 1) ?? -1;
            return secondAmp != -1 ? instancePart?[..secondAmp] : instancePart;
        }

        private static string? ExtractPid(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "0000";
            // If it's a full HID/USB path, find the PID segment
            if (s.Contains("PID_")) return s.Substring(s.IndexOf("PID_") + 4, 4);
            return "0000";
        }
    }
}