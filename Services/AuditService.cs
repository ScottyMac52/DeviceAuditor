using DeviceAuditor.Models;
using DeviceAuditor.Services.Interfaces;
using Microsoft.Win32;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace DeviceAuditor.Services
{
    /// <summary>
    /// Improved AuditService that properly handles composite HID devices (multiple MI_ / ColXX entries)
    /// such as the VID_8089&PID_000C keyboard/mouse combo.
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

        public AuditService(IDeviceDatabase db, IRepairService repair)
        {
            _db = db;
            _repair = repair;
        }

        public void Run(Options opts)
        {
            Console.WriteLine("===========================================================");
            Console.WriteLine(" MULTI-VENDOR PERIPHERAL AUDITOR - CTS v3.x ENGINE");
            Console.WriteLine("===========================================================\n");

            if (!_db.Load())
            {
                Console.WriteLine("Error: devices.json missing or invalid.");
                return;
            }

            string[] vendorList = opts.Vendors.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var activeList = new List<DeviceSummary>();
            var inactiveList = new List<DeviceSummary>();

            foreach (var vid in vendorList)
            {
                string cleanVid = vid.Trim().ToUpper();

                Console.WriteLine($"Scanning VID_{cleanVid}...");

                // 1. ACTIVE devices (WMI)
                var activeDevices = ScanActiveDevices(cleanVid);
                activeList.AddRange(activeDevices);

                // 2. INACTIVE / Ghost devices (Registry)
                if (!opts.ActiveOnly)
                {
                    var activeKeys = activeDevices.Select(d => d.InstanceID)
                                                 .Where(id => !string.IsNullOrEmpty(id))
                                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var ghosts = GetGhostsFromRegistry(cleanVid, activeKeys);
                    inactiveList.AddRange(ghosts);
                }
            }

            ProcessCategory("ACTIVE HID DEVICES", activeList, opts.Fix);
            if (!opts.ActiveOnly)
                ProcessCategory("INACTIVE / GHOST DEVICES", inactiveList, opts.Fix);

            Console.WriteLine("\nScan Complete. Press any key to exit.");
            if (!Console.IsInputRedirected) Console.ReadKey();
        }

        #region Active Scan (WMI)

        private List<DeviceSummary> ScanActiveDevices(string cleanVid)
        {
            var devices = new List<DeviceSummary>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var query = $@"SELECT DeviceID, Caption, ContainerID 
                           FROM Win32_PnPEntity 
                           WHERE ConfigManagerErrorCode = 0 
                           AND (DeviceID LIKE 'HID\\VID_{cleanVid}%' OR DeviceID LIKE '%VID_{cleanVid}%')";

            using var searcher = new ManagementObjectSearcher(query);

            foreach (ManagementObject node in searcher.Get())
            {
                string? fullId = node["DeviceID"]?.ToString();
                string? containerId = node["ContainerID"]?.ToString();
                string? caption = node["Caption"]?.ToString();

                if (string.IsNullOrEmpty(fullId)) continue;

                string instanceKey = GetBestInstanceKey(fullId, containerId);
                if (seen.Contains(instanceKey)) continue;
                seen.Add(instanceKey);

                string? pid = ExtractPid(fullId);
                var pwrInfo = GetParentPowerInfo(fullId);

                devices.Add(new DeviceSummary
                {
                    Name = _db.GetName(pid, cleanVid) ?? $"Unknown {cleanVid} Device",
                    InstanceID = instanceKey,
                    Status = pwrInfo.Status,
                    RegistryPath = pwrInfo.Path,
                    Caption = caption
                });
            }

            return devices;
        }

        #endregion

        #region Ghost / Inactive Scan (Registry)

        private List<DeviceSummary> GetGhostsFromRegistry(string vid, HashSet<string> activeInstanceKeys)
        {
            var ghosts = new List<DeviceSummary>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var hidKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\HID");
            if (hidKey == null) return ghosts;

            foreach (var pidKeyName in hidKey.GetSubKeyNames()
                .Where(x => x.Contains($"VID_{vid}", StringComparison.OrdinalIgnoreCase)))
            {
                using var pidKey = hidKey.OpenSubKey(pidKeyName);
                if (pidKey == null) continue;

                foreach (var instanceName in pidKey.GetSubKeyNames())
                {
                    var fullHidId = $@"HID\{pidKeyName}\{instanceName}";
                    string? containerId = GetContainerId(fullHidId);
                    string instanceKey = containerId ?? ExtractPhysicalRootFromInstance(instanceName);

                    if (activeInstanceKeys.Contains(instanceKey) || seen.Contains(instanceKey))
                        continue;

                    seen.Add(instanceKey);

                    string? pid = ExtractPid(pidKeyName);
                    var pwrInfo = GetParentPowerInfo(fullHidId);

                    ghosts.Add(new DeviceSummary
                    {
                        Name = _db.GetName(pid, vid) ?? $"Unknown {vid} Device",
                        InstanceID = instanceKey,
                        Status = pwrInfo.Status,
                        RegistryPath = pwrInfo.Path
                    });
                }
            }

            return ghosts;
        }

        #endregion

        #region Helper Methods

        private static string GetContainerId(string instanceId)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{instanceId}");
                return key?.GetValue("ContainerID")?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Best effort unique key for a physical device (ContainerID preferred)
        /// </summary>
        private static string GetBestInstanceKey(string? fullId, string? containerId)
        {
            if (!string.IsNullOrEmpty(containerId))
                return containerId;

            return ExtractPhysicalRoot(fullId) ?? fullId ?? "Unknown";
        }

        private static (int Status, string? Path) GetParentPowerInfo(string hidInstanceId)
        {
            if (string.IsNullOrWhiteSpace(hidInstanceId))
                return (-1, null);

            uint dnDevInst;
            int cr = CM_Locate_DevNode(out dnDevInst, hidInstanceId, CM_LOCATE_DEVNODE_NORMAL);
            if (cr != CR_SUCCESS)
            {
                cr = CM_Locate_DevNode(out dnDevInst, hidInstanceId, CM_LOCATE_DEVNODE_PHANTOM);
                if (cr != CR_SUCCESS)
                    return (-1, null);
            }

            uint parentInst;
            if (CM_Get_Parent(out parentInst, dnDevInst, 0) != CR_SUCCESS)
                return (-1, null);

            var buffer = new StringBuilder(1024);
            if (CM_Get_Device_ID(parentInst, buffer, buffer.Capacity, 0) != CR_SUCCESS)
                return (-1, null);

            string parentId = buffer.ToString();
            string fullPath = $@"SYSTEM\CurrentControlSet\Enum\{parentId}\Device Parameters";

            using var pKey = Registry.LocalMachine.OpenSubKey(fullPath, false);
            if (pKey != null)
            {
                var val = pKey.GetValue("EnhancedPowerManagementEnabled");
                int status = val is int i ? i : (val != null ? Convert.ToInt32(val) : -1);
                return (status, fullPath);
            }

            return (-1, fullPath);
        }

        private static string? ExtractPhysicalRoot(string? fullId)
        {
            if (string.IsNullOrEmpty(fullId)) return null;
            var parts = fullId.Split('\\');
            return parts.Length > 0 ? ExtractPhysicalRootFromInstance(parts.Last()) : null;
        }

        private static string? ExtractPhysicalRootFromInstance(string? instancePart)
        {
            if (string.IsNullOrEmpty(instancePart)) return null;

            int firstAmp = instancePart.IndexOf('&');
            if (firstAmp == -1) return instancePart;

            int secondAmp = instancePart.IndexOf('&', firstAmp + 1);
            return secondAmp != -1 ? instancePart[..secondAmp] : instancePart;
        }

        private static string? ExtractPid(string? s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int idx = s.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return null;
            return s.Substring(idx + 4, 4);
        }

        #endregion

        #region Output

        private void ProcessCategory(string title, List<DeviceSummary> devices, bool autoFix)
        {
            Console.WriteLine($"\n--- {title} ---");
            if (devices.Count == 0)
            {
                Console.WriteLine("None detected.");
                return;
            }

            foreach (var dev in devices)
            {
                PrintDeviceLine(dev);
                if (autoFix && dev.Status == 1)
                {
                    bool success = _repair.FixDevice(dev);
                    if (success)
                        Console.WriteLine("      → EnhancedPowerManagementEnabled = 0 (fix applied)");
                }
            }
        }

        private static void PrintDeviceLine(DeviceSummary s)
        {
            Console.Write($"{s.Name,-40} | ID: {s.InstanceID,-20} | PWR: ");
            if (s.Status == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("OPTIMAL");
            }
            else if (s.Status == 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("UNSTABLE");
            }
            else
            {
                Console.Write("DEFAULT");
            }
            Console.ResetColor();
            Console.WriteLine();
        }

        #endregion
    }
}