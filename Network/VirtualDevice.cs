using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkManager
{
  public class VirtualDevice
  {
    internal Device.BaseDevice Device;
    public VirtualDevice() { IP = LastIp + 1; LastIp = IP; MachineName += 1; }
    private static uint LastIp = 0;
    public string MachineName = "VirtualDevice";
    /// <summary>
    /// Something like that "sim://xxxxxxx"
    /// </summary>
    public string Address;
    public uint IP;
    /// <summary>
    /// Set this value to "false" to disconnect the simulated connection to the virtual machine interner. 
    /// When this value is "true" then the connection to the virtual network simulates will work correctly.
    /// </summary>
    public bool IsOnline { get { return NetSpeed != 0f; } set { if (value) NetSpeed = _NS != 0 ? _NS : 1; else _NS = NetSpeed; NetSpeed = 0; } }
    private float _NS;
    /// <summary>
    /// Simulated internet speed in megabytes per second.
    /// Set this value to zero to simulate the disconnection of the virtual internet network.
    /// </summary>
    public float NetSpeed = 10;
    public void SetIP(string IP)
    {
      this.IP = Converter.IpToUint(IP);
    }
  }
}
