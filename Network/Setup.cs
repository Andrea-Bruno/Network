using System;
using System.Collections.Generic;
using System.Text;


namespace NetworkManager
{
  public static class Setup
  {
    public static NetworkConfiguration Network = new NetworkConfiguration();
    public class NetworkConfiguration
    {
      public string NetworkName = "testnet";
      public string MachineName = Environment.MachineName;
      public string MasterServer;
      public string MasterServerMachineName;
    }
    public static class Ambient
    {
      public static string Repository = "blockchains";
    }
  }

}
