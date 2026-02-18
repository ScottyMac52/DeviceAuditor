namespace DeviceAuditor.Models
{
    /// <summary>
    /// Defines a detected device
    /// </summary>
    public class DeviceSummary
    {
        /// <summary>
        /// Device Name
        /// </summary>
        public string? Name { get; set; }
        /// <summary>
        /// Devie Instance ID
        /// </summary>
        public string? InstanceID { get; set; }
        /// <summary>
        /// Device Status
        /// </summary>
        public int? Status { get; set; }
        /// <summary>
        /// Path for IRepairService to target
        /// </summary>
        public string? RegistryPath { get; internal set; }
    }
}
