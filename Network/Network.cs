using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace NetworkManager
{
  public class Network : Device
  {
    /// <summary>
    /// This method initializes the network.
    /// You can join the network as a node, and contribute to decentralization, or hook yourself to the network as an external user.
    /// To create a node, set the MyAddress parameter with your web address.If MyAddress is not set then you are an external user.
    /// </summary>
    /// <param name="EntryPoints">The list of permanent access points nodes, to access the network</param>
    /// <param name="NetworkName">The name of the infrastructure. For tests we recommend using "testnet"</param>
    /// <param name="MyAddress">Your web address. If you do not want to create the node, omit this parameter</param>
    /// <param name="VirtualDevice">Optional parameter used to create a virtual machine for testing. The virtual machine helps the developer to create a simulated dummy network in the machine used for development. It is thus possible to create multiple nodes by simulating a p2p network. The list of already instanced devices is obtained through Network.Devices</param>
    public Network(Node[] EntryPoints, string NetworkName = "testnet", string MyAddress = null, VirtualDevice VirtualDevice = null) : base(VirtualDevice)
    {
      //if (VirtualDevice != null)
      //{
      //  //base = new Device() { VirtualDevice = VirtualDevice };
      //}
      Comunication = new Comunication(this, null);
      NodeList = new List<Node>(EntryPoints);
      Networks.Add(this);
      _MyAddress = MyAddress;
      _NetworkName = NetworkName;
      Protocol = new Protocol(this);
      BufferManager = new BufferManager(this);
      MappingNetwork = new MappingNetwork(this);
      //Setup.Network.MyAddress = MyAddress;
      //Setup.Network.NetworkName = NetworkName;
      //if (EntryPoints != null)
      //  Setup.Network.EntryPoints = EntryPoints;
      OnReceivesHttpRequest = base.OnReceivesHttpRequest;
      OnlineDetection.WaitForInternetConnection();
    }
    internal void Start()
    {
      if (NodeList != null)
      {
        var EntryPointsList = NodeList.ToList();
        EntryPointsList.RemoveAll(x => x.Address == _MyAddress);
        NodeList = EntryPointsList;
      }
      var ConnectionNode = GetRandomNode();
      var Nodes = Protocol.GetNetworkNodes(ConnectionNode);
      if (Nodes != null && Nodes.Count > 0)
        NodeList = Nodes;
      int NetworkLatency = 0;
      lock (NodeList)
      {
        if (!string.IsNullOrEmpty(MyAddress))
        {
          MyNode = new Node(MachineName, _MyAddress);
          var Stats1 = Protocol.GetStats(GetRandomNode());
          var Stats2 = Protocol.GetStats(GetRandomNode());
          NetworkLatency = Math.Max(Stats1.NetworkLatency, Stats2.NetworkLatency);
          MappingNetwork.SetNetworkSyncTimeSpan(NetworkLatency);
          var Answare = Protocol.ImOnline(ConnectionNode, MyNode);
          // if Answare = NoAnsware then I'm the first online node in the network  
          NodeList.RemoveAll(x => x.Address == MyNode.Address);
          NodeList.Add(MyNode);
        }
        NodeList = NodeList.OrderBy(o => o.Address).ToList();
      }
      if (MyNode != null)
      {
        MappingNetwork.SetNodeNetwork();
        MappingNetwork.SetNetworkSyncTimeSpan(NetworkLatency);
      }

    }
    //==== REMOVE THE TEST IN THE FINAL VERSION
    public void Test()
    {
      var L = 2;
      var H = 1;
      var list = new List<Node>();
      for (int y = 0; y < H; y++)
      {
        for (int x = 0; x < L; x++)
        {
          var Node = new Node() { Address = x.ToString() + "," + y.ToString() };
          list.Add(Node);
        }
      }
      NodeList = list;
      MyNode = GetRandomNode();
      MappingNetwork.SetNodeNetwork();
      MappingNetwork.GetXY(MyNode, out int X, out int Y);
      var mynode = MappingNetwork.GetNodeAtPosition(X, Y);
      var connections = MappingNetwork.GetConnections(1);
    }
    private string _MyAddress;
    public string MyAddress { get { return _MyAddress; } }
    private string _NetworkName;
    public string NetworkName { get { return _NetworkName; } }
    private string _MasterServerMachineName;
    public string MasterServerMachineName { get { return _MasterServerMachineName; } }
    public class Node
    {
      public Node() { }
      /// <summary>
      /// Used only to create MyNode. Generate an RSA for the current node.
      /// </summary>
      /// <param name="MachineName">Name of this Machine</param>
      /// <param name="Address">Address of this node</param>
      internal Node(string MachineName, string Address)
      {
        this.MachineName = MachineName;
        this.Address = Address;
        //Create RSA
        var RSA = new System.Security.Cryptography.RSACryptoServiceProvider();
        var PublicKeyBase64 = Convert.ToBase64String(RSA.ExportCspBlob(false));
        _RSA = RSA;

      }
      public string Address;
      public string MachineName;
      public string PublicKey;
      private System.Security.Cryptography.RSACryptoServiceProvider _RSA;
      public System.Security.Cryptography.RSACryptoServiceProvider RSA
      {
        get
        {
          if (_RSA == null)
            _RSA = new System.Security.Cryptography.RSACryptoServiceProvider();
          _RSA.ImportCspBlob(Convert.FromBase64String(PublicKey));
          return _RSA;
        }
      }
      public uint IP;
      public void DetectIP()
      {
        try
        {
          var ips = System.Net.Dns.GetHostAddresses(new Uri(Address).Host);
          IP = Converter.IpToUint(ips.Last().ToString());
        }
        catch (Exception)
        {
        }
      }
    }
    internal List<Node> NodeList;
    internal Node GetRandomNode()
    {
      lock (NodeList)
      {
        int min = 1;
        if (MyNode != null)
          min = 2;
        if (NodeList.Count >= min)
        {
          Node RandomNode;
          do
          {
            RandomNode = NodeList[new Random().Next(NodeList.Count)];
          } while (RandomNode == MyNode);
          return RandomNode;
        }
      }
      return null;
    }
    internal Node MyNode;

    /// <summary>
    /// Performs a specific code addressed to a randomly selected node.
    /// </summary>
    /// <param name="Execute">The instructions to be executed</param>
    public bool InteractWithRandomNode(Execute Execute)
    {
      bool Ok;
      int TryNode = 0;
      do
      {
        var Node = GetRandomNode();
        int Count = 0;
        do
        {
          Count++;
          Ok = Execute.Invoke(Node);
        } while (Ok == false && Count < 2);
      } while (Ok == false && TryNode < 3);
      return Ok;
    }
    public delegate bool Execute(Node Node);
    /// <summary>
    /// Contains the logic that establishes a mapping of the network and its subdivision to increase its performance.
    /// The network is divided at a logical level into many ring groups, with a recursive pyramidal structure
    /// </summary>
    internal MappingNetwork MappingNetwork;
    /// <summary>
    /// The buffer is a mechanism that allows you to distribute objects on the network by assigning a timestamp.
    /// The objects inserted in the buffer will exit from it on all the nodes following the chronological order of input(they come out sorted by timestamp).
    /// The data output from the buffar must be managed by actions that are programmed when the network is initialized.
    /// </summary>
    internal readonly BufferManager BufferManager;
    /// <summary>
    /// Send an object to the network to be inserted in the shared buffer
    /// </summary>
    /// <param name="Object">Object to send</param>
    /// <returns></returns>
    public bool AddToSaredBuffer(object Object)
    {
      return Protocol.AddToSharedBuffer(GetRandomNode(), Object) == Protocol.StandardAnsware.Ok;
    }
    public delegate void SyncData(string XmlObject, long Timestamp);
    /// <summary>
    /// Add a action used to local sync the objects coming from the buffer
    /// </summary>
    /// <param name="Action">Action to execute for every object</param>
    /// <param name="ForObjectName">Indicates what kind of objects will be treated by this action</param>
    public bool AddSyncDataFromBufferAction(SyncData Action, string ForObjectName)
    {
      return BufferManager.AddSyncDataAction(Action, ForObjectName);
    }
    /// <summary>
    /// It creates communication mechanisms that can be used by a protocol to communicate between nodes via the Internet
    /// </summary>
    public readonly Comunication Comunication;
    public readonly Protocol Protocol;
    /// <summary>
    /// This procedure receives an http request and processes the response based on the input received and the protocol
    /// </summary>
    /// <param name="QueryString">QueryString Collection</param>
    /// <param name="Form">Form Collection</param>
    /// <param name="FromIP">the IP of who generated the request</param>
    /// <param name="ContentType">The ContentType of the answer</param>
    /// <param name="OutputStream">The stream to which the reply will be sent</param>
    /// <returns>True if the operation was successful</returns>
    [Obsolete("We recommend using this method from the Device class because each device could handle multiple networks", false)]
    public new OnReceivesHttpRequestDelegate OnReceivesHttpRequest;
  }
}
