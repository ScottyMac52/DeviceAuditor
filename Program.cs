using System;
using System.Management;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;

class PeripheralAuditor
{
    // Added Winwing (4098) to the list of monitored Vendors
    private static readonly string[] VendorIDs = { "044F", "4098" };
    private static Dictionary<string, string> DeviceDatabase = new Dictionary<string, string>();

    static void Main()
    {
        Console.WriteLine("===========================================================");
        Console.WriteLine(" MULTI-VENDOR PERIPHERAL AUDITOR - CTS v3.10 ENGINE");
        Console.WriteLine("===========================================================\n");

        if (!LoadDeviceDatabase()) return;

        try
        {
            var activeList = new List<DeviceSummary>();
            var inactiveList = new List<DeviceSummary>();

            foreach (var vid in VendorIDs)
            {
                // 1. COLLECT ACTIVE DEVICES VIA WMI
                var searcher = new ManagementObjectSearcher($@"SELECT DeviceID, Caption FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 0 AND DeviceID LIKE 'HID\\VID_{vid}%'");
                var activeNodes = searcher.Get().Cast<ManagementObject>().ToList();
                activeList.AddRange(ProcessNodes(activeNodes));

                // 2. COLLECT INACTIVE DEVICES VIA REGISTRY
                inactiveList.AddRange(GetInactiveFromRegistry(vid, activeList.Select(a => a.InstanceID).ToList()));
            }

            Console.WriteLine("--- ACTIVE HID GAME CONTROLLERS ---");
            if (!activeList.Any()) Console.WriteLine("No active controllers detected.");
            foreach (var dev in activeList) PrintDeviceLine(dev);

            Console.WriteLine("\n--- INACTIVE / HIDDEN CONTROLLERS ---");
            if (!inactiveList.Any()) Console.WriteLine("No inactive controllers found.");
            foreach (var dev in inactiveList) PrintDeviceLine(dev);
        }
        catch (Exception ex) { Console.WriteLine($"\nCritical Error: {ex.Message}"); }

        Console.WriteLine("\nScan Complete. Press any key to exit.");
        Console.ReadKey();
    }

    static List<DeviceSummary> GetInactiveFromRegistry(string vid, List<string> activeIds)
    {
        var inactive = new List<DeviceSummary>();
        string hidPath = @"SYSTEM\CurrentControlSet\Enum\HID";

        using (RegistryKey hidKey = Registry.LocalMachine.OpenSubKey(hidPath))
        {
            if (hidKey == null) return inactive;

            foreach (var pidKeyName in hidKey.GetSubKeyNames().Where(x => x.Contains($"VID_{vid}")))
            {
                using (RegistryKey pidKey = hidKey.OpenSubKey(pidKeyName))
                {
                    foreach (var instanceName in pidKey.GetSubKeyNames())
                    {
                        string rootId = ExtractPhysicalRootFromInstance(instanceName);

                        if (!activeIds.Contains(rootId, StringComparer.OrdinalIgnoreCase))
                        {
                            string pid = ExtractPid(pidKeyName);
                            if (!DeviceDatabase.TryGetValue(pid, out string friendlyName))
                                friendlyName = $"Unknown {vid} Device ({pid})";

                            int pwrStatus = GetParentPowerStatus(vid, pidKeyName, instanceName);

                            if (!inactive.Any(x => x.InstanceID.Equals(rootId, StringComparison.OrdinalIgnoreCase)))
                            {
                                inactive.Add(new DeviceSummary { Name = friendlyName, InstanceID = rootId, Status = pwrStatus });
                            }
                        }
                    }
                }
            }
        }
        return inactive;
    }

    static List<DeviceSummary> ProcessNodes(IEnumerable<ManagementObject> nodes)
    {
        var list = new List<DeviceSummary>();
        foreach (var node in nodes)
        {
            if (!node["Caption"].ToString().Contains("game controller", StringComparison.OrdinalIgnoreCase)) continue;

            string id = node["DeviceID"].ToString();
            string pid = ExtractPid(id);
            string vid = ExtractVid(id);
            string root = ExtractPhysicalRoot(id);

            if (!DeviceDatabase.TryGetValue(pid, out string name)) name = $"Unknown {vid} ({pid})";
            int pwr = GetParentPowerStatus(vid, ExtractPidKey(id), id.Split('\\').Last());

            if (!list.Any(x => x.InstanceID == root))
                list.Add(new DeviceSummary { Name = name, InstanceID = root, Status = pwr });
        }
        return list;
    }

    static int GetParentPowerStatus(string vid, string pidKey, string instancePart)
    {
        try
        {
            // Reconstruct the USB parent path to check power management
            string usbId = $@"USB\VID_{vid}&{pidKey}\{instancePart}";
            int lastAmp = usbId.LastIndexOf('&');
            if (lastAmp != -1) usbId = usbId.Substring(0, lastAmp);

            using (var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{usbId}\Device Parameters"))
            {
                if (key == null) return -1;
                var val = key.GetValue("EnhancedPowerManagementEnabled");
                return val != null ? Convert.ToInt32(val) : -1;
            }
        }
        catch { return -1; }
    }

    static string ExtractPhysicalRoot(string fullId) => ExtractPhysicalRootFromInstance(fullId.Split('\\').Last());

    static string ExtractPhysicalRootFromInstance(string instancePart)
    {
        int secondAmp = instancePart.IndexOf('&', instancePart.IndexOf('&') + 1);
        return secondAmp != -1 ? instancePart.Substring(0, secondAmp) : instancePart;
    }

    static void PrintDeviceLine(DeviceSummary s)
    {
        Console.Write($"{s.Name,-38} | ID: {s.InstanceID,-15} | PWR: ");
        if (s.Status == 0) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("OPTIMAL"); }
        else if (s.Status == 1) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("UNSTABLE"); }
        else Console.WriteLine("DEFAULT");
        Console.ResetColor();
    }

    static string ExtractVid(string s)
    {
        int i = s.IndexOf("VID_");
        return i != -1 ? s.Substring(i + 4, 4) : "0000";
    }

    static string ExtractPid(string s)
    {
        int i = s.IndexOf("PID_");
        return i != -1 ? s.Substring(i + 4, 4) : "0000";
    }

    static string ExtractPidKey(string s)
    {
        string[] parts = s.Split('\\');
        return parts.Length > 1 ? parts[1].Split('&').Last() : "";
    }

    static bool LoadDeviceDatabase()
    {
        string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices.json");
        if (!File.Exists(p)) return false;
        try { DeviceDatabase = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p)); return true; }
        catch { return false; }
    }

    class DeviceSummary { public string Name; public string InstanceID; public int Status; }
}