using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;

namespace NetworkManager
{
  public class NodeList : List<Node>
  {
    public NodeList(Network network)
    {
      _network = network;
    }
    private readonly Network _network;

    public new void Add(Node node)
    {
      lock (this)
      {
        if (node.timestamp != null)
          Add(node, (long)node.timestamp);
        else
          base.Add(node);
        Changed();
      }
    }
    public new void AddRange(IEnumerable<Node> nodes)
    {
      lock (this)
      {
        foreach (var node in nodes)
          if (node.timestamp != null)
            Add(node, (long)node.timestamp);
          else
            base.Add(node);
        Changed();
      }
    }
    /// <summary>
    /// Update the list of online nodes by querying the network.
    /// </summary>
    public void Update()
    {
      if (_network.MyNode == null)
        _update();
      else
      {
        //The list of nodes is updated when this node is also online, so the operation is postponed.
        var timer = new Timer(DeltaTimeMs) { AutoReset = false };
        timer.Elapsed += (sender, e) => _update();
        timer.Start();
      }
    }
    /// <summary>
    /// Update the list of online nodes by querying the network
    /// </summary>
    private void _update()
    {
      var connectionNode = _network.GetRandomNode();
      var nodes = _network.Protocol.GetNetworkNodes(connectionNode);
      if (_network.MyNode == null)
      {
        AddRange(nodes);
        return;
      }
      nodes.RemoveAll(x => x.Address == _network.MyNode.Address || x.Ip == _network.MyNode.Ip);
      Clear();
      AddRange(nodes);
      Add(_network.MyNode);
    }

    public new void Clear()
    {
      lock (this)
      {
        base.Clear();
        Changed();
      }
    }

    public new bool Remove(Node node)
    {
      return Remove(node.Ip);
    }

    private bool Remove(uint nodeIp)
    {
      lock (this)
      {
        lock (_comingSoon)
          _comingSoon.Remove(_comingSoon.Find(x => x.Ip == nodeIp));
        var node = base.Find(x => x.Ip == nodeIp);
        if (node != null)
        {
          lock (RecentOfflineNodes)
            RecentOfflineNodes.Add((node));
          var timer = new Timer(60000) { AutoReset = false };
          timer.Elapsed += (sender, e) =>
          {
            lock (RecentOfflineNodes) RecentOfflineNodes.Remove(node);
          };
          timer.Start();
          base.Remove(node);
          Changed(true);
          return true;
        }
        else
          return false;
      }
    }

    private void Changed(bool removed = false)
    {
      if (!removed) Sort((x, y) => x.Ip.CompareTo(y.Ip));
      //Network.NodeList = this.OrderBy(o => o.Ip).ToList();
      _network.MappingNetwork.SetNodeNetwork();
      _network.ThisNode.RaiseEventOnNodeListChanged(Count);
    }
    private const int DeltaTimeMs = 30000; //Add this node after these milliseconds from timestamp
    /// <summary>
    ///   Add a new node to the network. Using this function, the new node will be added to all nodes simultaneously.
    /// </summary>
    /// <param name="node">Node to add</param>
    /// <param name="timestamp">Timestamp of the notification</param>
    internal void Add(Node node, long timestamp)
    {
      //the node will be online starting from timestamp + DeltaTimeMs;
      node.timestamp = timestamp;
      lock (_comingSoon) _comingSoon.Add(node);
      var enabledFrom = timestamp + TimeSpan.FromMilliseconds(DeltaTimeMs).Ticks;
      var remainingTime = enabledFrom - _network.Now.Ticks;
      var ms = (int)TimeSpan.FromTicks(remainingTime).TotalMilliseconds;
      var timer = new Timer(ms >= 1 ? ms : 1) { AutoReset = false };
      timer.Elapsed += (sender, e) =>
      {
        lock (_comingSoon)
        {
          if (!_comingSoon.Contains(node)) return;
          node.timestamp = null;
          Add(node);
          _comingSoon.Remove(node);
        }
      };
      timer.Start();
    }
    /// <summary>
    /// Returns the list of currently active nodes, plus those that are imminent online.
    /// The nodes that will be imminent online, have set the timestamp, so that who receives this list knows when to activate the communication with these nodes.
    /// </summary>
    /// <returns></returns>
    public List<Node> ListWithComingSoon()
    {
      lock (this)
        lock (_comingSoon)
          return this.Concat(_comingSoon).ToList();
    }
    private readonly List<Node> _comingSoon = new List<Node>();
    public readonly List<Node> RecentOfflineNodes = new List<Node>();
    public List<Node> CurrentAndRecentNodes()
    {
      lock (this)
        lock (RecentOfflineNodes)
          return this.Concat(RecentOfflineNodes).ToList();
    }
  }
}