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
    public MappingNetwork(Network network)
    {
      _network = network;
    }
    private readonly Network _network;
    private int _c;
    private int _s;
    private int SquareSide(int count)
    {
      if (count == _c)
        return _s;
      _s = (int)Math.Ceiling(Math.Sqrt(count));
      _c = count;
      return _s;
    }
    private int _myX = 0;
    private int _myY = 0;
    internal TimeSpan NetworkSyncTimeSpan;
    internal void SetNetworkSyncTimeSpan(int latency)
    {
      if (latency != 0)
        NetworkSyncTimeSpan = TimeSpan.FromMilliseconds(latency * 1.2);
    }
    internal void GetXy(Node node, List<Node> nodeList, out int x, out int y)
    {
      if (nodeList.Count == 0)
      {
        x = -1;
        y = -1;
      }
      else
      {
        var position = nodeList.IndexOf(node);
        var side = SquareSide(nodeList.Count);
        x = position % side;
        y = (int)position / side;
      }
    }

    internal Node GetNodeAtPosition(List<Node> nodeList, int x, int y)
    {
      var id = (y * SquareSide(nodeList.Count) + x);
      return id >= 0 && id < nodeList.Count ? nodeList[id] : nodeList[Mod(id, nodeList.Count)];
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
      //SquareSide = (int)Math.Ceiling(Math.Sqrt(Network.NodeList.Count));
      GetXy(_network.MyNode, _network.NodeList, out _myX, out _myY);
      CacheConnections = new Dictionary<int, List<Node>>();
    }
    internal Dictionary<int, List<Node>> CacheConnections = new Dictionary<int, List<Node>>();
    /// <summary>
    /// All connections that have the node at a certain level
    /// </summary>
    /// <param name="level">The level is base 1</param>
    /// <returns>The list of nodes connected to the level</returns>
    internal List<Node> GetConnections(int level)
    {
      lock (CacheConnections)
      {
        if (CacheConnections.TryGetValue(level, out var result))
          return result;
        result = GetConnections(level, _network.MyNode, _network.NodeList);
        CacheConnections.Add(level, result);
        return result;
      }
    }
    internal List<Node> GetConnections(int level, Node node, List<Node> nodeList)
    {
      // return GetConnections(Level, X, Y);
      var list = new List<Node>();
      if (nodeList.Count == 0) return list;
      var distance = SquareSide(nodeList.Count) / (int)Math.Pow(3, level);
      if (distance < 1)
        distance = 1;
      int xNode, yNode;
      if (node != _network.MyNode || nodeList != _network.NodeList)
        GetXy(node, nodeList, out xNode, out yNode);
      else
      {
        xNode = _myX;
        yNode = _myY;
      }

      for (var upDown = -1; upDown <= 1; upDown++)
        for (var leftRight = -1; leftRight <= 1; leftRight++)
          if (leftRight != 0 || upDown != 0)
          {
            var x = xNode + distance * leftRight;
            var y = yNode + distance * upDown;
            var connection = GetNodeAtPosition(nodeList, x, y);
            if (connection != node && !list.Contains(connection))
              list.Add(connection);
          }
      return list;
    }
  }

}
