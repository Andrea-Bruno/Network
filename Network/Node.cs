using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkManager
{
  public class Node
  {
    public Node() { }
    /// <summary>
    /// Used only to create MyNode. Generate an RSA for the current node.
    /// </summary>
    /// <param name="myNode">Parameters for this node</param>
    internal Node(NodeInitializer myNode)
    {
      Address = myNode.Address;
      MachineName = myNode.VirtualDevice?.MachineName ?? Environment.MachineName;
      //Create RSA
      var rsa = new System.Security.Cryptography.RSACryptoServiceProvider();
      rsa.ImportCspBlob(Convert.FromBase64String(myNode.PrivateKey));
      PublicKey = Convert.ToBase64String(rsa.ExportCspBlob(false));
      _rsa = rsa;
    }
    public string Address;
    public string MachineName;
    public string PublicKey;
    private System.Security.Cryptography.RSACryptoServiceProvider _rsa;
    internal System.Security.Cryptography.RSACryptoServiceProvider Rsa
    {
      get
      {
        if (_rsa != null) return _rsa;
        _rsa = new System.Security.Cryptography.RSACryptoServiceProvider();
        _rsa.ImportCspBlob(Convert.FromBase64String(PublicKey));
        return _rsa;
      }
    }
    /// <summary>
    /// The timestamp of when the node has notified its presence.
    /// Since the node is not put online immediately, this parameter is used to communicate to the other nodes, the nodes of the network in a list with nodes that will be imminent online but still waiting.
    /// </summary>
    public long? timestamp = null;
    public uint Ip;
    internal bool CheckIp()
    {
      return Ip == DetectIp();
    }
    internal uint DetectIp()
    {
      try
      {
        var virtualDevice = Device.FindDeviceByAddress(Address);
        if (virtualDevice != null)
          return virtualDevice.Ip;
        else
        {
          var ips = System.Net.Dns.GetHostAddresses(new Uri(Address).Host);
          return BitConverter.ToUInt32(ips[ips.Length-1].GetAddressBytes(), 0);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.Print(ex.Message);
        System.Diagnostics.Debugger.Break();
      }
      return 0;
    }
  }

  public class NodeInitializer
  {
    public NodeInitializer() { }
    /// <summary>
    /// Initialize the node to run in the virtual machine. Parameter used to make network tests by developers.
    /// </summary>
    /// <param name="virtualDevice"></param>
    public NodeInitializer(VirtualDevice virtualDevice) { VirtualDevice = virtualDevice; Address = virtualDevice.Address; }
    public string Address;
    public string PrivateKey;
    /// <summary>
    /// Optional parameter used to create a virtual machine for testing. The virtual machine helps the developer to create a simulated dummy network in the machine used for development. It is thus possible to create multiple nodes by simulating a p2p network. The list of already instanced devices is obtained through Network.Devices
    /// </summary>
    public VirtualDevice VirtualDevice;
  }

}
