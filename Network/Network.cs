using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace NetworkManager
{
  public class Network
  {
    /// <summary>
    /// This method initializes the network.
    /// You can join the network as a node, and contribute to decentralization, or hook yourself to the network as an external user.
    /// To create a node, set the MyAddress parameter with your web address.If MyAddress is not set then you are an external user.
    /// </summary>
    /// <param name="EntryPoints">The list of permanent access points nodes, to access the network</param>
    /// <param name="NetworkName">The name of the infrastructure. For tests we recommend using "testnet"</param>
    /// <param name="MyAddress">Your web address. If you do not want to create the node, omit this parameter</param>
    public Network(Node[] EntryPoints, string NetworkName = "testnet", string MyAddress = null)
    {
      Comunication = new Comunication(NetworkName, null);
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
      Protocol.OnlineDetection.WaitForInternetConnection();
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
    internal static List<Network> Networks = new List<Network>();
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
    public static string MachineName { get { return Environment.MachineName; } }
    private string _MasterServerMachineName;
    public string MasterServerMachineName { get { return _MasterServerMachineName; } }
    internal static bool _IsOnline;
    public static bool IsOnline { get { return _IsOnline; } }
    internal static DateTime Now()
    {
      return DateTime.UtcNow;
    }
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
    public static bool OnReceivesHttpRequest(System.Collections.Specialized.NameValueCollection QueryString, System.Collections.Specialized.NameValueCollection Form, string FromIP, out string ContentType, System.IO.Stream OutputStream)
    {
      ContentType = null;
      var NetworkName = QueryString["network"];
      foreach (var Network in Networks)
      {
        if (NetworkName == Network.NetworkName)
          if (Network._OnReceivesHttpRequest(QueryString, Form, FromIP, out ContentType, OutputStream))
            return true;
      }
      return false;
    }
    private bool _OnReceivesHttpRequest(System.Collections.Specialized.NameValueCollection QueryString, System.Collections.Specialized.NameValueCollection Form, string FromIP, out string ContentType, System.IO.Stream OutputStream)
    {
      var AppName = QueryString["app"];
      var ToUser = QueryString["touser"];
      var FromUser = QueryString["fromuser"];
      var Post = QueryString["post"];
      var Request = QueryString["request"];
      int.TryParse(QueryString["sectimeout"], out int SecTimeout);
      int.TryParse(QueryString["secwaitanswer"], out int SecWaitAnswer);

      string XmlObject = null;
      if (Form["object"] != null)
        XmlObject = Converter.Base64ToString(Form["object"]);

      if (ToUser == MachineName || ToUser.StartsWith(MachineName + "."))
      {
        var Parts = ToUser.Split('.'); // [0]=MachineName, [1]=PluginName
        string PluginName = null;
        if (Parts.Length > 1)
        {
          PluginName = Parts[1];
        }
        object ReturnObject = null;
        try
        {
          if (!string.IsNullOrEmpty(PluginName))
          {
            //foreach (PluginManager.Plugin Plugin in AllPlugins())
            //{
            //  if (Plugin.IsEnabled(Setting))
            //  {
            //    if (Plugin.Name == PluginName)
            //    {
            //      Plugin.PushObject(Post, XmlObject, Form, ReturnObject);
            //      break;
            //    }
            //  }
            //}
          }
          else
          {
            if (!string.IsNullOrEmpty(Post))//Post is a GetType Name
            {
              //Is a object trasmission
              if (string.IsNullOrEmpty(FromUser))
                ReturnObject = "error: no server name setting";
              else if (Protocol.OnReceivingObjectsActions.ContainsKey(Post))
                ReturnObject = Protocol.OnReceivingObjectsActions[Post](XmlObject);
            }
            if (!string.IsNullOrEmpty(Request))
            //Is a request o object
            {
              if (Protocol.OnRequestActions.ContainsKey(Request))
                ReturnObject = Protocol.OnRequestActions[Request](XmlObject);
              if (Protocol.StandardMessages.TryParse(Request, out Protocol.StandardMessages Rq))
              {
                if (Rq == Protocol.StandardMessages.NetworkNodes)
                  ReturnObject = NodeList;
                else if (Rq == Protocol.StandardMessages.GetStats)
                {
                  ReturnObject = new Protocol.Stats { NetworkLatency = (int)BufferManager.Stats24h.NetworkLatency.TotalMilliseconds };
                }
                else if (Rq == Protocol.StandardMessages.SendElementsToNode)
                  if (Converter.XmlToObject(XmlObject, typeof(List<BufferManager.ObjToNode>), out object ObjElements))
                  {
                    var UintFromIP = Converter.IpToUint(FromIP);
                    var FromNode = NodeList.Find((x) => x.IP == UintFromIP);
                    if (FromNode != null)
                    {
                      ReturnObject = BufferManager.AddLocalFromNode((List<BufferManager.ObjToNode>)ObjElements, FromNode);
                      if (ReturnObject == null)
                        ReturnObject = Protocol.StandardAnsware.Ok;
                    }
                  }
                  else
                    ReturnObject = Protocol.StandardAnsware.Error;

                else if (Rq == Protocol.StandardMessages.SendTimestampSignatureToNode)
                  if (Converter.XmlToObject(XmlObject, typeof(BufferManager.ObjToNode.TimestampVector), out object TimestampVector))
                  {
                    var UintFromIP = Converter.IpToUint(FromIP);
                    var FromNode = NodeList.Find((x) => x.IP == UintFromIP);
                    if (FromNode != null)
                    {
                      if (BufferManager.UnlockElementsInStandBy((BufferManager.ObjToNode.TimestampVector)TimestampVector, FromNode))
                        ReturnObject = Protocol.StandardAnsware.Ok;
                      else
                        ReturnObject = Protocol.StandardAnsware.Error;
                    }
                  }
                  else
                    ReturnObject = Protocol.StandardAnsware.Error;

                else if (Rq == Protocol.StandardMessages.AddToBuffer)
                  if (BufferManager.AddLocal(XmlObject) == true)
                    ReturnObject = Protocol.StandardAnsware.Ok;
                  else
                    ReturnObject = Protocol.StandardAnsware.Error;
                else if (Rq == Protocol.StandardMessages.ImOffline || Rq == Protocol.StandardMessages.ImOnline)
                  if (Converter.XmlToObject(XmlObject, typeof(Node), out object ObjNode))
                  {
                    var Node = (Node)ObjNode;
                    ReturnObject = Protocol.StandardAnsware.Ok;
                    if (Rq == Protocol.StandardMessages.ImOnline)
                    {
                      Node.DetectIP();
                      if (NodeList.Select(x => x.IP == Node.IP && Node.IP != Converter.IpToUint("127.0.0.1")) != null)
                        ReturnObject = Protocol.StandardAnsware.DuplicateIP;
                      else
                      {
                        if (Protocol.SpeedTest(Node))
                          lock (NodeList)
                          {
                            NodeList.Add(Node);
                            NodeList = NodeList.OrderBy(o => o.Address).ToList();
                          }
                        else
                          ReturnObject = Protocol.StandardAnsware.TooSlow;
                      }
                    }
                  }
                  else
                    ReturnObject = Protocol.StandardAnsware.Error;
                else if (Rq == Protocol.StandardMessages.TestSpeed)
                  ReturnObject = new string('x', 1048576);
                ContentType = "text/xml;charset=utf-8";
                XmlSerializer xml = new XmlSerializer(ReturnObject.GetType());
                XmlSerializerNamespaces xmlns = new XmlSerializerNamespaces();
                xmlns.Add(string.Empty, string.Empty);
                xml.Serialize(OutputStream, ReturnObject, xmlns);
                return true;
              }
            }
          }
        }
        catch (Exception ex)
        {
          ReturnObject = ex.Message;
        }
        if (ReturnObject != null && string.IsNullOrEmpty(Request))
        {
          var Vector = new Comunication.ObjectVector(ToUser, ReturnObject);
          ContentType = "text/xml;charset=utf-8";
          XmlSerializer xml = new XmlSerializer(typeof(Comunication.ObjectVector));
          XmlSerializerNamespaces xmlns = new XmlSerializerNamespaces();
          xmlns.Add(string.Empty, string.Empty);
          xml.Serialize(OutputStream, Vector, xmlns);
          return true;
        }
      }
      //if (Post != "")
      //  var se = new SpolerElement(AppName, FromUser, ToUser, QueryString("post"), XmlObject, SecTimeout);

      //if (string.IsNullOrEmpty(Request))
      //  SendObject(AppName, FromUser, ToUser, SecWaitAnswer);
      //else
      //  switch (Request)
      //  {
      //    default:
      //      break;
      //  }

      ContentType = null;
      return false;
    }
    /// <summary>
    /// It creates communication mechanisms that can be used by a protocol to communicate between nodes via the Internet
    /// </summary>
    public readonly Comunication Comunication;
    public readonly Protocol Protocol;
  }
}
