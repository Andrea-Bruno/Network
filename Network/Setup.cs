﻿using System;
using System.Collections.Generic;
using System.Text;
using NetworkManager;

namespace NetworkManager
{
  public class Setup
  {
    public class NetworkConfiguration
    {
      public string NetworkName = "testnet";
      public string MyAddress = null;
      public string MachineName = Environment.MachineName;
      public string MasterServer;
      public string MasterServerMachineName;
      public Node[] EntryPoints = new Node[2] { new Node() { Address = "http://localhost:62430", MachineName = Environment.MachineName, PublicKey = "" }, new Node() { Address = "http://localhost:55008", MachineName = Environment.MachineName, PublicKey = "" } };
    }
    public class Ambient
    {
      public static string Repository = "blockchains";
    }
  }

}
