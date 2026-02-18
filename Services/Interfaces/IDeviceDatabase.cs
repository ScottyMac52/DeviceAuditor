using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeviceAuditor.Services.Interfaces
{
    /// <summary>
    /// Contract for the Device Database
    /// </summary>
    public interface IDeviceDatabase
    {
        /// <summary>
        /// Loads devices to attempt to ID
        /// </summary>
        /// <returns></returns>
        bool Load();
        /// <summary>
        /// Gets the Name of a device using it's VID-Vendor ID and PID
        /// </summary>
        /// <param name="pid"></param>
        /// <param name="vid"></param>
        /// <returns></returns>
        string GetName(string pid, string vid);
    }
}
