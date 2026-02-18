using CommandLine;

namespace DeviceAuditor.Models
{

    public class Options
    {
        [Option('v', "vendors", Required = false, HelpText = "Comma-separated list of Vendor IDs to audit. Current defaults are 044F-Thrustmaster 4098-WINWING")]
        public string Vendors { get; set; } = "044F,4098";

        [Option('a', "active-only", Required = false, HelpText = "Only report active controllers.")]
        public bool ActiveOnly { get; set; }

        [Option('f', "fix", Required = false, HelpText = "Attempt to fix UNSTABLE power management settings. Requires Administrative permissions!")]
        public bool Fix { get; set; }
    }
}
