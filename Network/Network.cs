using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using static NetworkManager.Protocol;

namespace NetworkManager
{
  public class Network : Device
  {
    public delegate bool Execute(Node node);

    public delegate void SyncData(string xmlObject, long timestamp);

    /// <summary>
    ///   The buffer is a mechanism that allows you to distribute objects on the network by assigning a timestamp.
    ///   The objects inserted in the buffer will exit from it on all the nodes following the chronological order of input(they
    ///   come out sorted by timestamp).
    ///   The data output from the buffar must be managed by actions that are programmed when the network is initialized.
    /// </summary>
    internal readonly BufferManager BufferManager;

    /// <summary>
    ///   It creates communication mechanisms that can be used by a protocol to communicate between nodes via the Internet
    /// </summary>
    public readonly Comunication Comunication;

    public readonly Protocol Protocol;

    //internal InfoNode ThisNode;
    public readonly InfoNode ThisNode;


    //private string _MyAddress;
    //public string MyAddress { get { return _MyAddress; } }
    //public uint? MyIP { get { return MyNode?.IP; } }


    /// <summary>
    ///   Contains the logic that establishes a mapping of the network and its subdivision to increase its performance.
    ///   The network is divided at a logical level into many ring groups, with a recursive pyramidal structure
    /// </summary>
    internal MappingNetwork MappingNetwork;

    internal Node MyNode;
    internal List<Node> NodeList;
    [Obsolete("We recommend using this method from the Device class because each device could handle multiple networks", false)]
    public new OnReceivesHttpRequestDelegate OnReceivesHttpRequest;

    private readonly List<Node> _recentOfflineNodes = new List<Node>();

    /// <summary>
    ///   This method initializes the network.
    ///   You can join the network as a node, and contribute to decentralization, or hook yourself to the network as an
    ///   external user.
    ///   To create a node, set the MyAddress parameter with your web address.If MyAddress is not set then you are an external
    ///   user.
    /// </summary>
    /// <param name="entryPoints">The list of permanent access points nodes, to access the network</param>
    /// <param name="networkName">The name of the infrastructure. For tests we recommend using "testnet"</param>
    /// <param name="myNode">Data related to your node. If you do not want to create the node, omit this parameter</param>
    public Network(Node[] entryPoints, string networkName = "testnet", NodeInitializer myNode = null) : base(
      myNode?.VirtualDevice)
    {
      //if (VirtualDevice != null)
      //{
      //  //base = new Device() { VirtualDevice = VirtualDevice };
      //}
      Comunication = new Comunication(this);
      NodeList = new List<Node>(entryPoints);
      Networks.Add(this);
      if (myNode != null)
      {
        MyNode = new Node(myNode);
        ThisNode = new InfoNode(MyNode);
      }

      //_MyAddress = MyAddress;
      NetworkName = networkName;
      Protocol = new Protocol(this);
      BufferManager = new BufferManager(this);
      MappingNetwork = new MappingNetwork(this);
      //Setup.Network.MyAddress = MyAddress;
      //Setup.Network.NetworkName = NetworkName;
      //if (EntryPoints != null)
      //  Setup.Network.EntryPoints = EntryPoints;
#pragma warning disable CS0618 // 'Network.OnReceivesHttpRequest' è obsoleto: 'We recommend using this method from the Device class because each device could handle multiple networks'
      OnReceivesHttpRequest = base.OnReceivesHttpRequest; //Is ok! Don't worry
#pragma warning restore CS0618 // 'Network.OnReceivesHttpRequest' è obsoleto: 'We recommend using this method from the Device class because each device could handle multiple networks'
      OnlineDetection.WaitForInternetConnection();
    }

    public string NetworkName { get; }
    public string MasterServerMachineName { get; }

    internal void Start()
    {
      var imEntryPoint = false;
      if (NodeList != null)
      {
        var entryPointsList = NodeList.ToList();
        entryPointsList.RemoveAll(x => x.Address == MyNode.Address);
        imEntryPoint = NodeList.Count != entryPointsList.Count;
        NodeList = entryPointsList;
      }

      var connectionNode = GetRandomNode();
      var nodes = Protocol.GetNetworkNodes(connectionNode);
      if (nodes != null && nodes.Count > 0)
        NodeList = nodes;
      var networkLatency = 0;
      lock (NodeList)
      {
        if (MyNode != null)
        {
          //MyNode = new Node(MachineName, _MyAddress);
          MyNode.Ip = VirtualDevice?.Ip ?? MyNode.DetectIp();
          var stats1 = Protocol.GetStats(GetRandomNode());
          var stats2 = Protocol.GetStats(GetRandomNode());
          networkLatency = Math.Max(stats1?.NetworkLatency ?? 0, stats2?.NetworkLatency ?? 0);
          MappingNetwork.SetNetworkSyncTimeSpan(networkLatency);
          ThisNode.ConnectionStatus = Protocol.ImOnline(connectionNode, MyNode);
          // if Answare = NoAnsware then I'm the first online node in the network  
          if (ThisNode.ConnectionStatus == StandardAnsware.NoAnsware && imEntryPoint)
            ThisNode.ConnectionStatus = StandardAnsware.Ok; //I'm the first online node
          NodeList.RemoveAll(x => x.Ip == MyNode.Ip);
          //NodeList.Add(new Node());
          NodeList.Add(MyNode);
        }

        NodeList = NodeList.OrderBy(o => o.Ip).ToList();
      }
      if (MyNode == null) return;
      MappingNetwork.SetNodeNetwork();
      MappingNetwork.SetNetworkSyncTimeSpan(networkLatency);
    }

    //==== REMOVE THE TEST IN THE FINAL VERSION
    public void Test()
    {
      var l = 2;
      var h = 1;
      var list = new List<Node>();
      for (var y = 0; y < h; y++)
        for (var x = 0; x < l; x++)
        {
          var node = new Node { Address = x + "," + y };
          list.Add(node);
        }

      NodeList = list;
      var myNode = GetRandomNode();
      MappingNetwork.SetNodeNetwork();
      MappingNetwork.GetXy(myNode, NodeList, out var x2, out var y2);
      var thisNode = MappingNetwork.GetNodeAtPosition(NodeList, x2, y2);
      var ok = MyNode == thisNode;
      var connections = MappingNetwork.GetConnections(1);
    }

    private void AddNode(Node node)
    {
      lock (NodeList)
      {
        NodeList.Add(node);
        NodeList = NodeList.OrderBy(o => o.Ip).ToList();
        MappingNetwork.SetNodeNetwork();
      }
    }

    /// <summary>
    ///   Add a new node to the network. Using this function, the new node will be added to all nodes simultaneously.
    /// </summary>
    /// <param name="node">Node to add</param>
    /// <param name="timestamp">Timestamp of the notification</param>
    internal void AddNode(Node node, long timestamp)
    {
      var deltaTimeMs = 30000; //Add this node after these milliseconds from timestamp
      var ms = deltaTimeMs - (int)new TimeSpan(Now.Ticks - timestamp).TotalMilliseconds;
      var timer = new Timer(ms >= 1 ? ms : 1) { AutoReset = false };
      timer.Elapsed += (sender, e) => { AddNode(node); };
      timer.Start();
    }

    internal List<Node> CurrentNodes()
    {
        return NodeList.Concat(_recentOfflineNodes).ToList();
    }

    internal bool ValidateConnectionAtLevel0(uint ipNodeAtLevel0, List<Node> connections)
    {
      var nodeAtLevel0 = CurrentNodes().Find(x => x.Ip == ipNodeAtLevel0);
      return nodeAtLevel0 != null && ValidateConnectionAtLevel0(nodeAtLevel0, connections);
    }

    internal bool ValidateConnectionAtLevel0(Node nodeAtLevel0, List<Node> connections)
    {
      lock (NodeList)
        lock (_recentOfflineNodes)
        {
          List<Node> possibleConnectios;
          for (var n = 0; n <= _recentOfflineNodes.Count; n++)
            if (n == 0)
            {
              possibleConnectios = MappingNetwork.GetConnections(0, nodeAtLevel0, NodeList);
              if (possibleConnectios.Count == connections.Count &&
                  connections.TrueForAll(x => possibleConnectios.Contains(x)))
                return true;
            }
            else
            {
              var groupsNodesToAdd = (List<List<Node>>)Utility.GetPermutations(_recentOfflineNodes, n);
              foreach (var nodesToAdd in groupsNodesToAdd)
              {
                var list = NodeList.Concat(nodesToAdd).ToList();
                list = list.OrderBy(x => x.Ip).ToList();
                possibleConnectios = MappingNetwork.GetConnections(0, nodeAtLevel0, list);
                if (possibleConnectios.Count == connections.Count &&
                    connections.TrueForAll(x => possibleConnectios.Contains(x)))
                  return true;
              }
            }
        }

      return false;
    }

    internal Node GetRandomNode()
    {
      lock (NodeList)
      {
        var min = 1;
        if (MyNode != null)
          min = 2;
        if (NodeList.Count < min) return null;
        Node randomNode;
        do
        {
          randomNode = NodeList[new Random().Next(NodeList.Count)];
        } while (randomNode == MyNode);
        return randomNode;
      }
    }

    /// <summary>
    ///   Performs a specific code addressed to a randomly selected node.
    /// </summary>
    /// <param name="execute">The instructions to be executed</param>
    public bool InteractWithRandomNode(Execute execute)
    {
      bool ok;
      var tryNode = 0;
      do
      {
        var node = GetRandomNode();
        var count = 0;
        do
        {
          count++;
          ok = execute.Invoke(node);
        } while (ok == false && count < 2);
      } while (ok == false && tryNode < 3);

      return ok;
    }

    /// <summary>
    ///   Send an object to the network to be inserted in the shared buffer
    /// </summary>
    /// <param name="Object">Object to send</param>
    /// <returns></returns>
    public StandardAnsware AddToSaredBuffer(object Object)
    {
      return Protocol.AddToSharedBuffer(GetRandomNode(), Object);
    }

    /// <summary>
    ///   Add a action used to local sync the objects coming from the buffer
    /// </summary>
    /// <param name="action">Action to execute for every object</param>
    /// <param name="forObjectName">Indicates what kind of objects will be treated by this action</param>
    public bool AddSyncDataFromBufferAction(SyncData action, string forObjectName)
    {
      return BufferManager.AddSyncDataAction(action, forObjectName);
    }

    public class InfoNode
    {
      private readonly Node _base;
      private StandardAnsware _connectionStatus = StandardAnsware.Disconnected;

      public InfoNode(Node Base)
      {
        _base = Base;
      }

      public string Address => _base.Address;
      public string MachineName => _base.MachineName;
      public string PublicKey => _base.PublicKey;
      public uint Ip => _base.Ip;

      public StandardAnsware ConnectionStatus
      {
        get => _connectionStatus;
        internal set
        {
          _connectionStatus = value;
          OnConnectionStatusChanged?.Invoke(EventArgs.Empty, ConnectionStatus);
        }
      }

      public event EventHandler<StandardAnsware> OnConnectionStatusChanged;
    }
  }
}