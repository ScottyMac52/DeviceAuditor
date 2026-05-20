using DeviceAuditor.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;

public class DeviceDatabase : IDeviceDatabase
{
    private readonly ILogger<DeviceDatabase> _logger;
    private readonly Dictionary<string, Dictionary<string, string>> _db = new();

    public DeviceDatabase(ILogger<DeviceDatabase> logger)
    {
        _logger = logger;
    }

    public bool Load()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = $"{assembly.GetName().Name}.devices.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                _logger.LogCritical("Embedded resource not found: {ResourceName}", resourceName);
                return false;
            }

            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();

            var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
            if (data == null)
            {
                _logger.LogError("Failed to deserialize devices.json — invalid format");
                return false;
            }

            foreach (var kvp in data)
            {
                _db[kvp.Key.ToUpperInvariant()] = kvp.Value;
            }

            _logger.LogDebug("Loaded {Count} vendor entries from devices.json", _db.Count);
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parse error in devices.json");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading devices.json");
            return false;
        }
    }

    public string? GetName(string? pid, string? vid)
    {
        if (string.IsNullOrEmpty(pid) || string.IsNullOrEmpty(vid))
            return null;

        vid = vid.ToUpperInvariant();
        pid = pid.ToUpperInvariant();

        if (_db.TryGetValue(vid, out var pids) && pids.TryGetValue(pid, out var name))
        {
            return name;
        }

        return $"Unknown {vid} Device ({pid})";
    }
}