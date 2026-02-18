using DeviceAuditor.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeviceAuditor.Services.Interfaces
{
    /// <summary>
    /// Contract for the Audit
    /// </summary>
    public interface IAuditService
    {
        /// <summary>
        /// Runs the Audit
        /// </summary>
        /// <param name="opts"></param>
        void Run(Options opts);
    }
}
