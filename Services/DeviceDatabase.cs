using DeviceAuditor.Services.Interfaces;
using System.Text.Json;

namespace DeviceAuditor.Services
{
    /// <summary>
    /// Implements <see cref="IDeviceDatabase"/> 
    /// </summary>
    public class DeviceDatabase : IDeviceDatabase
    {
        private Dictionary<string, string> _db = new Dictionary<string, string>();

        /// <inheritdoc/>
        public bool Load()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices.json");
            if (!File.Exists(path))
            {
                Console.WriteLine("Error: devices.json not found in the application directory.");
                return false;
            }
            try
            {
                string json = File.ReadAllText(path);
                _db = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing devices.json: {ex.Message}");
                return false;
            }
        }

        /// <inheritdoc/>
        public string GetName(string pid, string vid)
        {
            if (_db.TryGetValue(pid, out string name)) return name;
            return $"Unknown {vid} Device ({pid})";
        }
    }
}
