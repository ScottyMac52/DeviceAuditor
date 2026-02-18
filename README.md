# ---

**Thrustmaster & WINWING Peripheral Auditor (v3.12)**

**DeviceAuditor** is a specialized diagnostic and repair tool for flight simulation enthusiasts. It identifies high-fidelity flight hardware, detects unstable power management settings (USB Selective Suspend), and provides an automated "Repair" mode to ensure your devices never disconnect mid-flight.

## **Features**

* **Hybrid Discovery**: Combines WMI and direct Registry scanning to find both active and "hidden" (ghosted) devices.  
* **Multi-Vendor Support**: Native support for **Thrustmaster (044F)** and **WINWING (4098)** hardware.  
* **Power Audit**: Checks the EnhancedPowerManagementEnabled registry key for every device.  
* **Automated Repair**: One-click fix to set unstable devices to OPTIMAL power mode.  
* **Physical Grouping**: Uses instance root logic to ensure complex devices appear as a single entry rather than multiple logical handles.

## **Usage**

Run the auditor from an elevated command prompt (Administrator) to enable the repair features.

### **Command Line Parameters**

| Parameter | Shorthand | Description |
| :---- | :---- | :---- |
| \--vendors | \-v | Comma-separated list of VIDs to audit (Default: 044F,4098). |
| \--active-only | \-a | Only show devices currently connected to the USB bus. |
| \--fix | \-f | Automatically set power management to 0 (Optimal) for detected gear. |
| \--help |  | Show the help screen. |

## **Sample Output**

Plaintext

\===========================================================  
 MULTI-VENDOR PERIPHERAL AUDITOR \- CTS v3.12 ENGINE  
\===========================================================

\--- ACTIVE HID GAME CONTROLLERS \---  
AVA F/A-18 Hornet Flightstick          | ID: 7&253E1DB3      | PWR: UNSTABLE  
      \-\> Applied FIX: EnhancedPowerManagementEnabled set to 0\.  
HOTAS Warthog Throttle                 | ID: 6&2380CD3D      | PWR: OPTIMAL  
WINWING PTO 2 (Take Off Panel)         | ID: 8&2C7A6B33      | PWR: DEFAULT

\--- INACTIVE / HIDDEN CONTROLLERS \---  
F-16 MFD 1 (LMFD)                      | ID: 8&29E81075      | PWR: DEFAULT  
HOTAS Warthog Joystick                 | ID: 7&3563C42A      | PWR: UNSTABLE

Scan Complete. Press any key to exit.

## **Troubleshooting Power States**

The PWR column indicates the current state of Windows USB Power Management for that specific hardware instance:

* **OPTIMAL**: EnhancedPowerManagementEnabled is set to 0\. Windows is forbidden from suspending this device.  
* **UNSTABLE**: EnhancedPowerManagementEnabled is set to 1\. Windows may put the device to sleep to save power, causing disconnections or "ghost inputs" in DCS and MSFS.  
* **DEFAULT**: The registry key does not exist. Windows uses global defaults (usually allowing suspension).

## **Known Issues**

### **Registry Reset after Windows Updates**

A common issue with Windows 10 and 11 is that **Feature Updates** or major cumulative updates can reset PnP (Plug and Play) registry keys. When Windows re-enumerates the USB bus during an update, it may recreate the Device Parameters subkeys with default values, effectively flipping an OPTIMAL device back to UNSTABLE.

**Recommendation:** Run the auditor after any major Windows Update to ensure your flight stack remains optimized.

### **USB Port Swapping**

Because the power management key is tied to the specific **Instance ID** (which includes the USB port path), moving a device to a different USB port or a different hub will result in Windows creating a brand new registry entry. This new entry will likely have power management enabled by default.

## **Installation & Requirements**

1. **Framework**: None. The released version is **Self-Contained** and includes all necessary runtimes.  
2. **Configuration**: Ensure devices.json is in the same folder as the executable.  
3. **Privileges**: Windows Administrator rights are required to use the \--fix parameter.

# ---