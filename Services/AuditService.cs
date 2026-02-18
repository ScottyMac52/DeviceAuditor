using DeviceAuditor.Models;
using DeviceAuditor.Services.Interfaces;
using Microsoft.Win32;
using System.Management;

namespace DeviceAuditor.Services
{
    /// <summary>
    /// Implements <see cref="IAuditService"/> 
    /// </summary>
    public class AuditService : IAuditService
    {
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

                // 1. ACTIVE SCAN (WMI)
                var searcher = new ManagementObjectSearcher($@"SELECT DeviceID, Caption FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 0 AND DeviceID LIKE 'HID\\VID_{cleanVid}%'");
                foreach (ManagementObject node in searcher.Get())
                {
                    if (!node["Caption"].ToString().Contains("game controller", StringComparison.OrdinalIgnoreCase)) continue;

                    string id = node["DeviceID"].ToString();
                    string pid = ExtractPid(id);
                    string root = ExtractPhysicalRoot(id);

                    var pwrInfo = GetParentPowerInfo(cleanVid, ExtractPidKey(id), id.Split('\\').Last());

                    if (!activeList.Any(x => x.InstanceID == root))
                        activeList.Add(new DeviceSummary { Name = _db.GetName(pid, cleanVid), InstanceID = root, Status = pwrInfo.Status, RegistryPath = pwrInfo.Path });
                }

                // 2. INACTIVE SCAN (REGISTRY)
                if (!opts.ActiveOnly)
                {
                    inactiveList.AddRange(GetGhostsFromRegistry(cleanVid, activeList.Select(a => a.InstanceID).ToList()));
                }
            }

            ProcessCategory("ACTIVE HID GAME CONTROLLERS", activeList, opts.Fix);
            if (!opts.ActiveOnly) ProcessCategory("INACTIVE / HIDDEN CONTROLLERS", inactiveList, opts.Fix);

            Console.WriteLine("\nScan Complete. Press any key to exit.");
            if (!Console.IsInputRedirected) Console.ReadKey();
        }

        private void ProcessCategory(string title, List<DeviceSummary> devices, bool autoFix)
        {
            Console.WriteLine($"--- {title} ---");
            if (!devices.Any()) { Console.WriteLine("None detected."); return; }

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
                        foreach (var instanceName in pidKey.GetSubKeyNames())
                        {
                            string rootId = ExtractPhysicalRootFromInstance(instanceName);
                            if (!activeIds.Contains(rootId, StringComparer.OrdinalIgnoreCase))
                            {
                                string pid = ExtractPid(pidKeyName);
                                var pwrInfo = GetParentPowerInfo(vid, pidKeyName.Split('&').Last(p => p.StartsWith("PID_")), instanceName);
                                if (!ghosts.Any(x => x.InstanceID.Equals(rootId, StringComparison.OrdinalIgnoreCase)))
                                    ghosts.Add(new DeviceSummary { Name = _db.GetName(pid, vid), InstanceID = rootId, Status = pwrInfo.Status, RegistryPath = pwrInfo.Path });
                            }
                        }
                    }
                }
            }
            return ghosts;
        }

        private (int Status, string Path) GetParentPowerInfo(string vid, string pidKey, string instancePart)
        {
            string usbId = $@"USB\VID_{vid}&{pidKey}\{instancePart}";
            int lastAmp = usbId.LastIndexOf('&');
            if (lastAmp != -1) usbId = usbId.Substring(0, lastAmp);
            string path = $@"SYSTEM\CurrentControlSet\Enum\{usbId}\Device Parameters";

            using (var key = Registry.LocalMachine.OpenSubKey(path))
            {
                if (key == null) return (-1, null);
                var val = key.GetValue("EnhancedPowerManagementEnabled");
                return (val != null ? Convert.ToInt32(val) : -1, path);
            }
        }

        private void PrintDeviceLine(DeviceSummary s)
        {
            Console.Write($"{s.Name,-38} | ID: {s.InstanceID,-15} | PWR: ");
            if (s.Status == 0) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("OPTIMAL"); }
            else if (s.Status == 1) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("UNSTABLE"); }
            else Console.WriteLine("DEFAULT");
            Console.ResetColor();
        }

        private string ExtractPid(string s) => s.Contains("PID_") ? s.Substring(s.IndexOf("PID_") + 4, 4) : "0000";
        private string ExtractPhysicalRoot(string fullId) => ExtractPhysicalRootFromInstance(fullId.Split('\\').Last());
        private string ExtractPhysicalRootFromInstance(string instancePart)
        {
            int secondAmp = instancePart.IndexOf('&', instancePart.IndexOf('&') + 1);
            return secondAmp != -1 ? instancePart.Substring(0, secondAmp) : instancePart;
        }
        private string ExtractPidKey(string s) => s.Split('\\').Length > 1 ? s.Split('\\')[1].Split('&').Last(p => p.StartsWith("PID_")) : "";
    }
}
