using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkManager
{
  public class VirtualDevice
  {
    internal Device.BaseDevice Device;
    public VirtualDevice() { Ip = _lastIp + 1; _lastIp = Ip; MachineName += 1; }
    private static uint _lastIp = 0;
    public string MachineName = "VirtualDevice";
    /// <summary>
    /// Something like that "sim://xxxxxxx"
    /// </summary>
    public string Address;
    public uint Ip;
    /// <summary>
    /// Set this value to "false" to disconnect the simulated connection to the virtual machine interner. 
    /// When this value is "true" then the connection to the virtual network simulates will work correctly.
    /// </summary>
    public bool IsOnline { get => NetSpeed != 0f;
      set { if (value) NetSpeed = _ns != 0 ? _ns : 1; else _ns = NetSpeed; NetSpeed = 0; } }
    private float _ns;
    /// <summary>
    /// Simulated internet speed in megabytes per second.
    /// Set this value to zero to simulate the disconnection of the virtual internet network.
    /// </summary>
    public float NetSpeed = 10;
    public void SetIp(string ip)
    {
      Ip = Converter.IpToUint(ip);
    }
  }
}
