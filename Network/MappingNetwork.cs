using System;
using System.Collections.Generic;
using System.Text;
using static NetworkManager.Network;

namespace NetworkManager
{
  /// <summary>
  /// Contains the logic that establishes a mapping of the network and its subdivision to increase its performance.
  /// The network is divided at a logical level into many ring groups, with a recursive pyramidal structure
  /// </summary>
  internal class MappingNetwork
  {
    public MappingNetwork(Network Network)
    {
      this.Network = Network;
    }
    private Network Network;
    private int SquareSide = 0;
    private int MyX = 0;
    private int MyY = 0;
    internal TimeSpan NetworkSyncTimeSpan;
    internal void SetNetworkSyncTimeSpan(int Latency)
    {
      if (Latency != 0)
        NetworkSyncTimeSpan = TimeSpan.FromMilliseconds(Latency * 1.2);
    }
    internal void GetXY(Node Node, out int X, out int Y)
    {
      var Position = Network.NodeList.IndexOf(Node);
      X = Position % SquareSide;
      Y = (int)Position / SquareSide;
    }

    internal Node GetNodeAtPosition(int X, int Y)
    {
      var id = (Y * SquareSide + X);
      if (id >= 0 && id < Network.NodeList.Count)
        return Network.NodeList[id];
      else
        return Network.NodeList[Mod(id, Network.NodeList.Count)];
    }

    /// <summary>
    /// Mod operator: Divides two numbers and returns only the remainder.
    /// NOTE: The calculation of the module with negative numbers in c # is wrong!
    /// </summary>
    /// <param name="a">Any numeric expression</param>
    /// <param name="b">Any numeric expression</param>
    /// <returns></returns>
    private int Mod(int a, int b)
    {
      return (int)(a - b * Math.Floor((double)a / (double)b));
    }
    internal void SetNodeNetwork()
    {
      SquareSide = (int)Math.Ceiling(Math.Sqrt(Network.NodeList.Count));
      GetXY(Network.MyNode, out MyX, out MyY);
      CacheConnections = new Dictionary<int, List<Node>>();
    }
    internal Dictionary<int, List<Node>> CacheConnections = new Dictionary<int, List<Node>>();
    /// <summary>
    /// All connections that have the node at a certain level
    /// </summary>
    /// <param name="Level">The level is base 1</param>
    /// <returns>The list of nodes connected to the level</returns>
    internal List<Node> GetConnections(int Level)
    {
      return GetConnections(Level, MyX, MyY);
    }
    internal List<Node> GetConnections(int Level, Node Node)
    {
      GetXY(Node, out int X, out int Y);
      return GetConnections(Level, X, Y);
    }
    private List<Node> GetConnections(int Level, int XNode, int YNode)
    {
      int Distance = SquareSide / (int)Math.Pow(3, Level);
      if (Distance < 1)
        Distance = 1;
      lock (CacheConnections)
      {
        if (XNode == MyX && YNode == MyY && CacheConnections.TryGetValue(Distance, out List<Node> List))
          return List;
        List = new List<Node>();
        for (int UpDown = -1; UpDown <= 1; UpDown++)
          for (int LeftRight = -1; LeftRight <= 1; LeftRight++)
            if (LeftRight != 0 || UpDown != 0)
            {
              var X = XNode + Distance * LeftRight;
              var Y = YNode + Distance * UpDown;
              if (X != XNode && Y != YNode)
              {
                var Connection = GetNodeAtPosition(X, Y);
                if (!List.Contains(Connection))
                  List.Add(Connection);
              }
            }
        if (XNode == MyX && YNode == MyY)
          CacheConnections.Add(Distance, List);
        return List;
      }
    }
  }

}
