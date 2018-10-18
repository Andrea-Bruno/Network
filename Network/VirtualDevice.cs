using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkManager
{
  public class VirtualDevice
  {
    internal Device.BaseDevice Device;
    public VirtualDevice() { IP = LastIp + 1; LastIp = IP; }
    private static uint LastIp = 0;
    public string MachineName = "VirtualDevice";
    /// <summary>
    /// Something like that "sim://xxxxxxx"
    /// </summary>
    public string Address;
    public uint IP;
    public void SetIP(string IP)
    {
      this.IP = Converter.IpToUint(IP);
    }
  }
}
