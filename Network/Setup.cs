using System;
using System.Collections.Generic;
using System.Text;
using static NetworkManager.Network;

namespace NetworkManager
{
  public static class Setup
  {
    public static NetworkConfiguration Network = new NetworkConfiguration();
    public class NetworkConfiguration
    {
      public string NetworkName = "testnet";
      public string MyAddress = "http://localhost:55007";
      public string MachineName = Environment.MachineName;
      public string MasterServer;
      public string MasterServerMachineName;
      public Node[] EntryPoints = new Node[1] { new Node() { Address = "http://localhost:55007", MachineName = Environment.MachineName, PublicKey = "" }};
    }
    public static class Ambient
    {
      public static string Repository = "blockchains";
    }
  }

}
