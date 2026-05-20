using DeviceAuditor.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeviceAuditor.Services.Interfaces
{
    /// <summary>
    /// Contract for device repair
    /// </summary>
    public interface IRepairService
    {
        /// <summary>
        /// Fixes the device described by <see cref="DeviceSummary"/>
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        bool FixDevice(DeviceSummary device);
    }
}
