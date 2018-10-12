using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
    /// <param name="MyAddress">Your web address. If you do not want to create the node, omit this parameter</param>
    /// <param name="EntryPoints">The list of permanent access points nodes, to access the network. If null then the entry points will be those set in the NetworkManager.Setup</param>
    /// <param name="NetworkName">The name of the infrastructure. For tests we recommend using "testnet"</param>
    public Network(Node[] EntryPoints, string NetworkName = "testnet", string MyAddress = null)
    {
      Comunication = new ComunicationClass(NetworkName, null);
      NodeList = new List<Node>(EntryPoints);
      Networks.Add(this);
      _MyAddress = MyAddress;
      _NetworkName = NetworkName;
      Protocol = new ProtocolClass(this);
      BufferManager = new BufferManagerClass(this);
      MappingNetwork = new MappingNetworkClass(this);
      //Setup.Network.MyAddress = MyAddress;
      //Setup.Network.NetworkName = NetworkName;
      //if (EntryPoints != null)
      //  Setup.Network.EntryPoints = EntryPoints;
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
          MyNode = new Node { MachineName = MachineName, Address = _MyAddress };
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

    private static List<Network> Networks = new List<Network>();
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
    public string MachineName { get { return Environment.MachineName; } }
    private string _MasterServerMachineName;
    public string MasterServerMachineName { get { return _MasterServerMachineName; } }
    private static bool _IsOnline;
    public static bool IsOnline { get { return _IsOnline; } }
    private static DateTime Now()
    {
      return DateTime.UtcNow;
    }
    public class Node
    {
      public string Address;
      public string MachineName;
      public string PublicKey;
      public string IP;
      public void DetectIP()
      {
        try
        {
          var ips = System.Net.Dns.GetHostAddresses(new Uri(Address).Host);
          IP = ips.Last().ToString();
        }
        catch (Exception)
        {
        }
      }
    }
    private List<Node> NodeList;
    private Node GetRandomNode()
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
    private Node MyNode;

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
    private MappingNetworkClass MappingNetwork;
    private class MappingNetworkClass
    {
      public MappingNetworkClass(Network Network)
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
        int Distance = SquareSide / (int)Math.Pow(3, Level);
        if (Distance < 1)
          Distance = 1;
        lock (CacheConnections)
        {
          if (CacheConnections.TryGetValue(Distance, out List<Node> List))
            return List;
          List = new List<Node>();
          for (int UpDown = -1; UpDown <= 1; UpDown++)
            for (int LeftRight = -1; LeftRight <= 1; LeftRight++)
              if (LeftRight != 0 || UpDown != 0)
              {
                var X = MyX + Distance * LeftRight;
                var Y = MyY + Distance * UpDown;
                if (X != MyX && Y != MyY)
                {
                  var Connection = GetNodeAtPosition(X, Y);
                  if (!List.Contains(Connection))
                    List.Add(Connection);
                }
              }
          CacheConnections.Add(Distance, List);
          return List;
        }
      }

    }
    /// <summary>
    /// The buffer is a mechanism that allows you to distribute objects on the network by assigning a timestamp.
    /// The objects inserted in the buffer will exit from it on all the nodes following the chronological order of input(they come out sorted by timestamp).
    /// The data output from the buffar must be managed by actions that are programmed when the network is initialized.
    /// </summary>
    public readonly BufferManagerClass BufferManager;
    public class BufferManagerClass
    {
      public BufferManagerClass(Network Network)
      {
        this.Network = Network;
        Spooler = new SpoolerClass(Network);
        BufferTimer = new System.Timers.Timer(1000) { AutoReset = true, Enabled = true };
        BufferTimer.Elapsed += (sender, e) => ToDoEverySec();

        NewStats = new System.Timers.Timer(43200000) { AutoReset = true, Enabled = true };//Every 12h
        NewStats.Elapsed += (sender, e) => NewStatsElapsed();

      }
      private Network Network;
      /// <summary>
      /// The spooler contains the logic that allows synchronization of data on the peer2peer network
      /// </summary>
      private SpoolerClass Spooler;
      private class SpoolerClass
      {
        public SpoolerClass(Network Network)
        {
          this.Network = Network;
          this.BufferManager = Network.BufferManager;
          this.MappingNetwork = Network.MappingNetwork;
          SpoolerTimer = new System.Timers.Timer(1000) { AutoReset = true, Enabled = true };
          SpoolerTimer.Elapsed += (sender, e) => DataDelivery();
        }
        private Network Network;
        private BufferManagerClass BufferManager;
        private MappingNetworkClass MappingNetwork;
        private int SpoolerTimeMs = 1000;
        private int PauseBetweenTransmissionOnTheNode = 2000;
        private Dictionary<Node, DateTime> LastTransmission = new Dictionary<Node, DateTime>();
        private System.Timers.Timer SpoolerTimer;
        //internal void DataDelivery()
        //{
        //  DataDelivery(null, null, this);
        //}
        internal void DataDelivery()
        {
          var DataToNode = new Dictionary<Node, List<BufferManagerClass.ObjToNode>>();
          lock (Network.BufferManager.Buffer)
            foreach (var Data in Network.BufferManager.Buffer)
              lock (Data.Levels)
                foreach (var Level in Data.Levels)
                  foreach (var Node in Network.MappingNetwork.GetConnections(Level))
                    if (!Data.SendedNode.Contains(Node))
                    {
                      var MillisecondsFromLastTransmission = int.MaxValue;
                      lock (LastTransmission)
                        if (LastTransmission.TryGetValue(Node, out DateTime TrasmissionTime))
                          MillisecondsFromLastTransmission = (int)(DateTime.UtcNow - TrasmissionTime).TotalMilliseconds;
                      if (MillisecondsFromLastTransmission > PauseBetweenTransmissionOnTheNode)
                      {
                        List<BufferManagerClass.ObjToNode> ToSendToNode;
                        if (!DataToNode.TryGetValue(Node, out ToSendToNode))
                        {
                          ToSendToNode = new List<BufferManagerClass.ObjToNode>();
                          DataToNode.Add(Node, ToSendToNode);
                        }
                        var Element = (BufferManagerClass.Element)Data;
                        var ElementToNode = (BufferManagerClass.ObjToNode)Element;
                        ElementToNode.Level = Level;
                        ToSendToNode.Add(ElementToNode);
                        lock (Data.SendedNode)
                          Data.SendedNode.Add(Node);
                      }
                    }
          foreach (var ToSend in DataToNode)
          {
            Network.Protocol.SendElementsToNode(ToSend.Value, ToSend.Key);
            lock (LastTransmission)
            {
              LastTransmission.Remove(ToSend.Key);
              LastTransmission.Add(ToSend.Key, DateTime.UtcNow);
            }
          }
        }
      }


      /// <summary>
      /// Send an object to the network to be inserted in the shared buffer
      /// </summary>
      /// <param name="Object">Object to send</param>
      /// <returns></returns>
      public bool AddToSaredBuffer(object Object)
      {
        return Network.Protocol.AddToSharedBuffer(Network.GetRandomNode(), Object) == ProtocolClass.StandardAnsware.Ok;
      }
      private System.Timers.Timer BufferTimer;
      private void ToDoEverySec()
      {
        //var Buffer = BufferManager.Buffer;
        lock (Buffer)
        {
          var ToRemove = new List<ElementBuffer>();
          var ThisTime = Now();
          foreach (var item in Buffer)
          {
            if ((ThisTime - item.Timestamp) > Network.MappingNetwork.NetworkSyncTimeSpan)
            {
              ToRemove.Add(item);
              var ObjectName = GetObjectName(item.XmlObject);
              if (BufferCompletedAction.TryGetValue(ObjectName, out SyncData Action))
                Action.Invoke(item.XmlObject, item.Timestamp);
              //foreach (SyncData Action in BufferCompletedAction)
              //  Action.Invoke(item.XmlObject, item.Timestamp);
            }
            else
              break;//because the beffer is sorted by Timespan
          }
          foreach (var item in ToRemove)
            Buffer.Remove(item);
        }
      }
      /// <summary>
      /// Insert the object in the local buffer to be synchronized
      /// XmlObject is a new element inserted by an external user
      /// </summary>
      /// <param name="XmlObject">Serialized object im format xml</param>
      /// <returns></returns>
      internal bool AddLocal(string XmlObject)
      {
        if (string.IsNullOrEmpty(XmlObject))
          return false;
        else
        {
          DateTime Timestamp = Now();
          var Element = new Element() { Timestamp = Timestamp, XmlObject = XmlObject };
          lock (Buffer)
          {
            var ElementBuffer = (ElementBuffer)Element;
            ElementBuffer.Levels.Add(1);
            Buffer.Add(ElementBuffer);
            SortBuffer();
          }
          Spooler.DataDelivery();
          return true;
        }
      }

      /// <summary>
      /// Insert the objects in the local buffer to be synchronized
      /// The elements come from other nodes
      /// </summary>
      /// <param name="Elements">Elements come from other nodes</param>
      internal void AddLocalFromNode(List<ObjToNode> Elements)
      {
        lock (Buffer)
        {
          var Count = Buffer.Count();
          var ThisTime = Now();
          foreach (var ObjToNode in Elements)
          {
            var TimePassedFromInsertion = ThisTime - ObjToNode.Timestamp;
            UpdateStats(TimePassedFromInsertion);
            if ((TimePassedFromInsertion) <= Network.MappingNetwork.NetworkSyncTimeSpan)
            {
              var Level = ObjToNode.Level + 1;
              var ElementBuffer = Buffer.Find((x) => x.Timestamp == ObjToNode.Timestamp && x.XmlObject == ObjToNode.XmlObject);
              if (ElementBuffer == null)
              {
                UpdateStats(TimePassedFromInsertion, true);
                ElementBuffer = (ElementBuffer)(Element)ObjToNode;
                Buffer.Add(ElementBuffer);
              }
              lock (ElementBuffer.Levels)
                if (!ElementBuffer.Levels.Contains(Level))
                  ElementBuffer.Levels.Add(Level);
              ElementBuffer.Received++;
            }
            else
            {
              //A dishonest node has started a fork through a fake timestamp?
              Stats24h.ElementsArrivedOutOfTime++;
              Stats12h.ElementsArrivedOutOfTime++;
            }
          }
          if (Count != Buffer.Count())
          {
            SortBuffer();
            Spooler.DataDelivery();
          }
        }
      }
      private void UpdateStats(TimeSpan Value, bool FirstAdd = false)
      {
        Stats24h.AddValue(Value, FirstAdd);
        Stats12h.AddValue(Value, FirstAdd);
      }
      private System.Timers.Timer NewStats;
      private void NewStatsElapsed()
      {
        Stats24h = Stats12h;
        Stats12h = new Statistics();
      }
      internal Statistics Stats24h = new Statistics();
      private Statistics Stats12h = new Statistics();
      internal class Statistics
      {
        internal void AddValue(TimeSpan Value, bool FirstAdd)
        {
          ReceivedElements++;
          if (FirstAdd)
          {
            MaximumArrivalTimeElement = Value;
            if (Value > MaximumArrivalTimeElement)
              ReceivedUnivocalElements++;
          }
          if (Value > NetworkLatency)
            NetworkLatency = Value;
        }
        internal int ReceivedElements = 0;
        internal int ReceivedUnivocalElements = 0;
        internal int ElementsArrivedOutOfTime = 0;
        internal TimeSpan MaximumArrivalTimeElement; /// Maximum time to update the node (from the first node)
        internal TimeSpan NetworkLatency; /// Maximum time to update the node (from all node)
      }
      void SortBuffer()
      {
        Buffer.OrderBy(x => x.XmlObject);//Used for the element whit same Timestamp
        Buffer.OrderBy(x => x.Timestamp);
      }
      internal List<ElementBuffer> Buffer = new List<ElementBuffer>();
      internal class ElementBuffer : Element
      {
        public List<Node> SendedNode = new List<Node>();
        public int Received;
        public List<int> Levels = new List<int>();
      }
      public class Element
      {
        public DateTime Timestamp;
        public string XmlObject;
      }
      public class ObjToNode : Element
      {
        public int Level;
      }
      public delegate void SyncData(string XmlObject, DateTime Timestamp);
      /// <summary>
      /// Add a action used to local sync the objects in the buffer
      /// </summary>
      /// <param name="Action">Action to execute for every object</param>
      /// <param name="ForObjectName">Indicates what kind of objects will be treated by this action</param>
      public bool AddSyncDataAction(SyncData Action, string ForObjectName)
      {
        if (BufferCompletedAction.ContainsKey(ForObjectName))
          return false;
        BufferCompletedAction.Add(ForObjectName, Action);
        return true;
      }
      private Dictionary<string, SyncData> BufferCompletedAction = new Dictionary<string, SyncData>();
      static private string GetObjectName(string XmlObject)
      {
        if (!string.IsNullOrEmpty(XmlObject))
        {
          var p1 = XmlObject.IndexOf('>');
          if (p1 != -1)
          {
            var p2 = XmlObject.IndexOf('<', p1);
            if (p2 != -1)
            {
              var p3 = XmlObject.IndexOf('>', p2);
              if (p3 != -1)
              {
                return XmlObject.Substring(p2 + 1, p3 - p2 - 1);
              }
            }
          }
        }
        return null;
      }
    }

    public static bool OnReceivesHttpRequest(System.Collections.Specialized.NameValueCollection QueryString, System.Collections.Specialized.NameValueCollection Form, string FromIP, out string ContentType, System.IO.Stream OutputStream)
    {
      ContentType = null;
      foreach (var Network in Networks)
      {
        if (Network._OnReceivesHttpRequest(QueryString, Form, FromIP, out ContentType, OutputStream))
          return true;
      }
      return false;
    }

    private bool _OnReceivesHttpRequest(System.Collections.Specialized.NameValueCollection QueryString, System.Collections.Specialized.NameValueCollection Form, string FromIP, out string ContentType, System.IO.Stream OutputStream)
    {
      //Setting = CurrentSetting();
      //Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);

      var NetworkName = QueryString["network"];
      //QueryString.TryGetValue("network", out string NetworkName);

      if (this.NetworkName == NetworkName)
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

                if (ProtocolClass.StandardMessages.TryParse(Request, out ProtocolClass.StandardMessages Rq))
                {
                  if (Rq == ProtocolClass.StandardMessages.NetworkNodes)
                    ReturnObject = NodeList;
                  else if (Rq == ProtocolClass.StandardMessages.GetStats)
                  {
                    ReturnObject = new ProtocolClass.Stats { NetworkLatency = (int)BufferManager.Stats24h.NetworkLatency.TotalMilliseconds };
                  }
                  else if (Rq == ProtocolClass.StandardMessages.SendElementsToNode)
                    if (Converter.XmlToObject(XmlObject, typeof(List<BufferManagerClass.ObjToNode>), out object ObjElements))
                    {
                      BufferManager.AddLocalFromNode((List<BufferManagerClass.ObjToNode>)ObjElements);
                      ReturnObject = ProtocolClass.StandardAnsware.Ok;
                    }
                    else
                      ReturnObject = ProtocolClass.StandardAnsware.Error;
                  else if (Rq == ProtocolClass.StandardMessages.AddToBuffer)
                    if (BufferManager.AddLocal(XmlObject) == true)
                      ReturnObject = ProtocolClass.StandardAnsware.Ok;
                    else
                      ReturnObject = ProtocolClass.StandardAnsware.Error;
                  else if (Rq == ProtocolClass.StandardMessages.ImOffline || Rq == ProtocolClass.StandardMessages.ImOnline)
                    if (Converter.XmlToObject(XmlObject, typeof(Node), out object ObjNode))
                    {
                      var Node = (Node)ObjNode;
                      ReturnObject = ProtocolClass.StandardAnsware.Ok;
                      if (Rq == ProtocolClass.StandardMessages.ImOnline)
                      {
                        Node.DetectIP();
                        if (Node.IP != "127.0.0.1" && NodeList.Select(x => x.IP == Node.IP) != null)
                          ReturnObject = ProtocolClass.StandardAnsware.DuplicateIP;
                        else
                        {
                          if (Protocol.SpeedTest(Node))
                            lock (NodeList)
                            {
                              NodeList.Add(Node);
                              NodeList = NodeList.OrderBy(o => o.Address).ToList();
                            }
                          else
                            ReturnObject = ProtocolClass.StandardAnsware.TooSlow;
                        }
                      }
                    }
                    else
                      ReturnObject = ProtocolClass.StandardAnsware.Error;
                  else if (Rq == ProtocolClass.StandardMessages.TestSpeed)
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
            var Vector = new ComunicationClass.ObjectVector(ToUser, ReturnObject);
            ContentType = "text/xml;charset=utf-8";
            XmlSerializer xml = new XmlSerializer(typeof(ComunicationClass.ObjectVector));
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
      }
      ContentType = null;
      return false;
    }
    public readonly ComunicationClass Comunication;
    public class ComunicationClass
    {
      public ComunicationClass(string NetworkName, string MasterServerMachineName)
      {
        this.NetworkName = NetworkName;
        this.MasterServerMachineName = MasterServerMachineName;
      }
      private string NetworkName;
      private string MasterServerMachineName;
      public string SendObjectSync(object Obj, string WebAddress = null, System.Collections.Specialized.NameValueCollection Dictionary = null, string ToUser = null, int SecTimeOut = 0, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
      {
        var Reader = ExecuteServerRequest(false, WebAddress, null, Obj, Dictionary, null, SecWaitAnswer, ExecuteIfNoAnswer, SecTimeOut, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
        return Reader.HTML;
      }
      public WebReader SendObjectAsync(ref object Obj, string WebAddress = null, System.Collections.Specialized.NameValueCollection Dictionary = null, string ToUser = null, int SecTimeOut = 0, int SecWaitAnswer = 0, OnReceivedObject ExecuteOnReceivedObject = null, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
      {
        return ExecuteServerRequest(true, WebAddress, null, Obj, Dictionary, ExecuteOnReceivedObject, SecWaitAnswer, ExecuteIfNoAnswer, SecTimeOut, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
      }
      public string GetObjectSync(string WebAddress = null, string Request = null, object Obj = null, string ToUser = null, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
      {
        var Reader = ExecuteServerRequest(false, WebAddress, Request, Obj, null, null, SecWaitAnswer, ExecuteIfNoAnswer, 0, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
        return Reader.HTML;
      }
      public WebReader GetObjectAsync(OnReceivedObject ExecuteOnReceivedObject, string WebAddress = null, string Request = null, object Obj = null, string ToUser = null, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
      {
        return ExecuteServerRequest(true, WebAddress, Request, Obj, null, ExecuteOnReceivedObject, SecWaitAnswer, ExecuteIfNoAnswer, 0, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
      }
      public delegate void OnReceivedObject(string FromUser, string ObjectName, string XmlObject);
      private string AppName = System.Reflection.Assembly.GetExecutingAssembly().FullName.Split(',')[0];
      private string UrlServer()
      {
        if (MasterServer == "")
          return null;
        return MasterServer.TrimEnd('/');
      }
      private string MasterServer;
      private WebReader ExecuteServerRequest(bool Async, string WebAddress = null, string Request = null, object Obj = null, System.Collections.Specialized.NameValueCollection Dictionary = null, OnReceivedObject ExecuteOnReceivedObject = null, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, int SecTimeOut = 0, string ToUser = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
      {
        if (WebAddress == null)
          WebAddress = UrlServer();
        WebAddress = WebAddress.TrimEnd('/');
        WebAddress += "?network=" + System.Uri.EscapeDataString(NetworkName) + "&app=" + System.Uri.EscapeDataString(AppName) + "&fromuser=" + System.Uri.EscapeDataString(Environment.MachineName) + "&secwaitanswer=" + SecWaitAnswer.ToString();
        if (CancellAllMyRequest)
          WebAddress += "&cancellrequest=true";
        if (RemoveObjectsToMe)
          WebAddress += "&removeobjects=true";
        if (RemoveMyObjects)
          WebAddress += "&removemyobjects=true";
        if (string.IsNullOrEmpty(ToUser))
          ToUser = MasterServerMachineName + ".";
        if (!string.IsNullOrEmpty(ToUser))
          WebAddress += "&touser=" + ToUser;
        if (!string.IsNullOrEmpty(Request))
          WebAddress += "&request=" + Request;

        Action<string> Parser = null;
        if (ExecuteOnReceivedObject != null)
        {
          Parser = (String Html) =>
          {
            ObjectVector ObjectVector = null;
            Converter.XmlToObject(Html, typeof(ObjectVector), out object ReturmObj);
            ObjectVector = (ObjectVector)ReturmObj;
            if (ObjectVector != null)
              ExecuteOnReceivedObject.Invoke(ObjectVector.FromUser, ObjectVector.ObjectName, ObjectVector.XmlObject);
          };
        }
        else
          WebAddress += "&nogetobject=true";

        if (Obj != null)
        {
          WebAddress += "&post=" + Obj.GetType().Name + "&sectimeout=" + SecTimeOut.ToString();
          System.IO.StringWriter Str = new System.IO.StringWriter();
          System.Xml.Serialization.XmlSerializer xml = new System.Xml.Serialization.XmlSerializer(Obj.GetType());
          System.Xml.Serialization.XmlSerializerNamespaces xmlns = new System.Xml.Serialization.XmlSerializerNamespaces();
          xmlns.Add(string.Empty, string.Empty);
          xml.Serialize(Str, Obj, xmlns);
          string postData = Str.ToString();
          if (Dictionary == null)
            Dictionary = new System.Collections.Specialized.NameValueCollection();
          string StrCod = Converter.StringToBase64(postData);
          Dictionary.Add("object", StrCod);
        }
        return ReadWeb(Async, WebAddress, Parser, null, Dictionary, SecWaitAnswer, ExecuteIfNoAnswer);
      }
      public static WebReader ReadWeb(bool Async, string Url, Action<string> Parser, Action Elapse, System.Collections.Specialized.NameValueCollection Dictionary = null, int SecTimeout = 0, Action ExecuteAtTimeout = null)
      {
        return new WebReader(Async, Url, Parser, Elapse, Dictionary, SecTimeout, ExecuteAtTimeout);
      }
      public class WebReader
      {
        public WebReader(bool Async, string Url, Action<string> Parser, Action Elapse, System.Collections.Specialized.NameValueCollection Dictionary = null, int SecTimeout = 0, Action ExecuteAtTimeout = null)
        {
          WebClient = new System.Net.WebClient();
          Execute = Parser;
          this.Elapse = Elapse;
          this.ExecuteAtTimeout = ExecuteAtTimeout;
          this.Dictionary = Dictionary;
          if (SecTimeout != 0)
          {
            Timeout = new System.Timers.Timer();
            Timeout.Interval = TimeSpan.FromSeconds(SecTimeout).TotalMilliseconds;
            Timeout.Start();
          }
          Start(Url, Async);
        }
        private System.Collections.Specialized.NameValueCollection Dictionary;
        private Action<string> Execute; // Parser = Sub(Html As String)
        private Action Elapse;
        private Action ExecuteAtTimeout;
        private System.Timers.Timer _Timeout;
        private System.Timers.Timer Timeout
        {
          [MethodImpl(MethodImplOptions.Synchronized)]
          get
          {
            return _Timeout;
          }

          [MethodImpl(MethodImplOptions.Synchronized)]
          set
          {
            if (_Timeout != null)
            {
              _Timeout.Elapsed -= Timeout_Tick;
            }

            _Timeout = value;
            if (_Timeout != null)
            {
              _Timeout.Elapsed += Timeout_Tick;
            }
          }
        }

        private void Timeout_Tick(object sender, System.EventArgs e)
        {
          WebClient.CancelAsync();
          if (ExecuteAtTimeout != null)
            ExecuteAtTimeout.Invoke();
        }
        public void CancelAsync()
        {
          if (Timeout != null)
            Timeout.Stop();
          WebClient.CancelAsync();
        }
        private System.Net.WebClient _WebClient;

        private System.Net.WebClient WebClient
        {
          [MethodImpl(MethodImplOptions.Synchronized)]
          get
          {
            return _WebClient;
          }

          [MethodImpl(MethodImplOptions.Synchronized)]
          set
          {
            if (_WebClient != null)
            {
              _WebClient.OpenReadCompleted -= WebClient_OpenReadCompleted;
            }

            _WebClient = value;
            if (_WebClient != null)
            {
              _WebClient.OpenReadCompleted += WebClient_OpenReadCompleted;
            }
          }
        }

        private void Start(string Url, bool Async)
        {
          if (Dictionary == null)
            Dictionary = new System.Collections.Specialized.NameValueCollection();
          if (Async)
            WebClient.UploadValuesAsync(new Uri(Url), "POST", Dictionary);
          //WebClient.OpenReadAsync(new Uri(Url));
          else
          {
            try
            {
              var responsebytes = WebClient.UploadValues(Url, "POST", Dictionary);
              HTML = (new System.Text.UTF8Encoding()).GetString(responsebytes);
            }
            catch (Exception ex)
            {
            }
            if (Execute != null && HTML != null)
              Execute(HTML);
            if (Elapse != null)
              Elapse();
          }
        }
        public string HTML;
        private void WebClient_OpenReadCompleted(object sender, System.Net.OpenReadCompletedEventArgs e)
        {
          if (Timeout != null)
            Timeout.Stop();
          if (e.Error == null && !e.Cancelled)
          {
            System.IO.BinaryReader BinaryStreamReader = new System.IO.BinaryReader(e.Result);
            byte[] Bytes;
            Bytes = BinaryStreamReader.ReadBytes(System.Convert.ToInt32(BinaryStreamReader.BaseStream.Length));
            if (Bytes != null)
            {
              var ContentType = WebClient.ResponseHeaders["Content-Type"];
              System.Text.Encoding Encoding = null;
              if (ContentType != "")
              {
                string[] Parts = ContentType.Split('=');
                if (Parts.Length == 2)
                {
                  try
                  {
                    Encoding = System.Text.Encoding.GetEncoding(Parts[1]);
                  }
                  catch (Exception ex)
                  {
                  }
                }
              }

              if (Encoding == null)
              {
                var Row = System.Text.Encoding.UTF8.GetString(Bytes, 0, Bytes.Length);
                if (Row != "")
                {
                  try
                  {
                    int P1 = Row.IndexOf("charset=") + 1;
                    if (P1 > 0)
                    {
                      if (Row[P1 + 7] == '"')
                        P1 += 9;
                      else
                        P1 += 8;
                      int P2 = Row.IndexOf("\"", P1);
                      if (P2 > 0)
                      {
                        var EncodeStr = Row.Substring(P1 - 1, P2 - P1);
                        try
                        {
                          Encoding = System.Text.Encoding.GetEncoding(EncodeStr); // http://msdn.microsoft.com/library/vstudio/system.text.encoding(v=vs.100).aspx
                        }
                        catch (Exception ex)
                        {
                        }
                      }
                    }
                  }
                  catch (Exception ex)
                  {
                  }
                }
              }
              if (Encoding != null)
              {
                HTML = Encoding.GetString(Bytes, 0, Bytes.Length);
                if (Execute != null && HTML != null)
                  Execute(HTML);
              }
            }
          }
          if (Elapse != null)
            Elapse();
        }
      }
      public class ObjectVector
      {
        public ObjectVector()
        {
        }
        public ObjectVector(string FromUser, string ObjectName, string XmlObject)
        {
          this.FromUser = FromUser; this.ObjectName = ObjectName; this.XmlObject = XmlObject;
        }
        public ObjectVector(string FromUser, object Obj)
        {
          this.FromUser = FromUser;
          ObjectName = Obj.GetType().Name;

          System.Xml.Serialization.XmlSerializer XmlSerializer = new System.Xml.Serialization.XmlSerializer(Obj.GetType());

          using (System.IO.StringWriter textWriter = new System.IO.StringWriter())
          {
            XmlSerializer.Serialize(textWriter, Obj);
            XmlObject = textWriter.ToString();
          }
        }
        public string FromUser;
        public string ObjectName;
        public string XmlObject;
      }

    }
    public readonly ProtocolClass Protocol;
    public class ProtocolClass
    {
      private Network Network;
      internal ProtocolClass(Network Network)
      {
        this.Network = Network;
      }
      public delegate object GetObject(string XmlObject);
      internal Dictionary<string, GetObject> OnReceivingObjectsActions = new Dictionary<string, GetObject>();
      public bool AddOnReceivingObjectAction(string NameTypeObject, GetObject GetObject)
      {
        lock (OnReceivingObjectsActions)
        {
          if (OnReceivingObjectsActions.ContainsKey(NameTypeObject))
            return false;
          else
            OnReceivingObjectsActions.Add(NameTypeObject, GetObject);
        }
        return true;
      }
      internal Dictionary<string, GetObject> OnRequestActions = new Dictionary<string, GetObject>();
      public bool AddOnRequestAction(string ActionName, GetObject GetObject)
      {
        lock (OnRequestActions)
        {
          if (OnRequestActions.ContainsKey(ActionName))
            return false;
          else
            OnRequestActions.Add(ActionName, GetObject);
        }
        return true;
      }
      private string NotifyToNode(Node ToNode, string Request, object Obj = null)
      {
        int Try = 0;
        string XmlResult;
        do
        {
          Try += 1;
          //if (ToNode == null)
          //  ToNode = GetRandomNode();
          if (ToNode == null)
            return "";
          XmlResult = Network.Comunication.GetObjectSync(ToNode.Address, Request, Obj, ToNode.MachineName + ".");
        } while (string.IsNullOrEmpty(XmlResult) && Try <= 10);
        if (Try > 10)
          _IsOnline = false;
        else
          _IsOnline = true;
        return XmlResult;
      }
      private string SendRequest(Node ToNode, StandardMessages Message, object Obj = null)
      {
        return NotifyToNode(ToNode, Message.ToString(), Obj);
      }
      internal enum StandardMessages { NetworkNodes, ImOnline, ImOffline, TestSpeed, AddToBuffer, SendElementsToNode, GetStats }
      public enum StandardAnsware { Ok, Error, DuplicateIP, TooSlow, Failure, NoAnsware, Declined }
      internal List<Node> GetNetworkNodes(Node EntryPoint)
      {
        var XmlResult = SendRequest(EntryPoint, StandardMessages.NetworkNodes);
        if (string.IsNullOrEmpty(XmlResult))
          return new List<Node>();
        Converter.XmlToObject(XmlResult, typeof(List<Node>), out object ReturmObj);
        List<Node> NodeList = (List<Node>)ReturmObj;
        return NodeList;
      }
      internal StandardAnsware ImOffline(Node ToNode, Node MyNode)
      {
        var XmlResult = SendRequest(ToNode, StandardMessages.ImOffline, MyNode);
        if (!string.IsNullOrEmpty(XmlResult))
          try
          {
            Converter.XmlToObject(XmlResult, typeof(StandardAnsware), out object Answare);
            return (StandardAnsware)Answare;
          }
          catch (Exception)
          {
          }
        return StandardAnsware.Error;
      }
      internal StandardAnsware ImOnline(Node ToNode, Node MyNode)
      {
        var XmlResult = SendRequest(ToNode, StandardMessages.ImOnline, MyNode);
        MyNode.DetectIP();
        //this.MyNode = MyNode;
        if (string.IsNullOrEmpty(XmlResult))
          return StandardAnsware.NoAnsware;
        try
        {
          Converter.XmlToObject(XmlResult, typeof(StandardAnsware), out object Answare);
          return (StandardAnsware)Answare;
        }
        catch (Exception)
        {
        }
        return StandardAnsware.Error;
      }
      public class Stats
      {

        /// <summary>
        /// Maximum time to update the node
        /// </summary>
        public int NetworkLatency;
      }
      internal Stats GetStats(Node FromNode)
      {
        var XmlResult = SendRequest(FromNode, StandardMessages.GetStats);
        if (string.IsNullOrEmpty(XmlResult))
          return null;
        Converter.XmlToObject(XmlResult, typeof(Stats), out object ReturmObj);
        var Stats = (Stats)ReturmObj;
        return Stats;
      }

      internal bool SpeedTest(Node NodeToTesting)
      {

        var Start = DateTime.UtcNow;

        for (int i = 0; i < 10; i++)
        {
          var XmlResult = SendRequest(NodeToTesting, StandardMessages.TestSpeed);
          if (XmlResult == null || XmlResult.Length != 1048616)
            return false;
        }
        var Speed = (DateTime.UtcNow - Start).TotalMilliseconds;
        return Speed <= 3000;
      }
      internal StandardAnsware AddToSharedBuffer(Node ToNode, Object Object)
      {
        try
        {
          var XmlResult = SendRequest(ToNode, StandardMessages.AddToBuffer, Object);
          //var XmlResult = Comunication.SendObjectSync(Object, Node.Address, null, Node.MachineName);
          if (string.IsNullOrEmpty(XmlResult))
            return StandardAnsware.NoAnsware;
          else
          {
            object ReturmObj;
            Converter.XmlToObject(XmlResult, typeof(StandardAnsware), out ReturmObj);
            StandardAnsware Answare = (StandardAnsware)ReturmObj;
            return Answare;
          }
        }
        catch (Exception)
        {
          return StandardAnsware.Error;
        }
      }
      private Dictionary<Node, int> FailureList = new Dictionary<Node, int>();
      internal void SendElementsToNode(List<BufferManagerClass.ObjToNode> Elements, Node ToNode)
      {
        new System.Threading.Thread(() =>
        {
          var XmlResult = SendRequest(ToNode, StandardMessages.SendElementsToNode, Elements);
          if (string.IsNullOrEmpty(XmlResult))
          {
            //the node is disconnected
            if (Network.NodeList.Contains(ToNode))
            {
              //try reconnect
              if (OnlineDetection.CheckImOnline())
              {
                int Attempts;
                lock (FailureList)
                {
                  if (FailureList.TryGetValue(ToNode, out Attempts))
                  {
                    FailureList.Remove(ToNode);
                    Attempts += 1;
                    FailureList.Add(ToNode, Attempts);
                  }
                  else
                  {
                    Attempts = 1;
                    FailureList.Add(ToNode, 1);
                  }
                }
                if (Attempts < 3)
                  SendElementsToNode(Elements, ToNode);
              }
              else
                OnlineDetection.WaitForInternetConnection();
            }
          }
          else
          {
            lock (FailureList)
              if (FailureList.ContainsKey(ToNode))
                FailureList.Remove(ToNode);
          }
        }).Start();
      }
    }
    private static class OnlineDetection
    {
      internal static bool CheckImOnline()
      {
        try
        {
          bool r1 = (new System.Net.NetworkInformation.Ping().Send("www.google.com.mx").Status == System.Net.NetworkInformation.IPStatus.Success);
          bool r2 = (new System.Net.NetworkInformation.Ping().Send("www.bing.com").Status == System.Net.NetworkInformation.IPStatus.Success);
          return r1 && r2;
        }
        catch (Exception)
        {
          return false;
        }
      }
      private static bool IsOnline;
      private static System.Threading.Timer CheckInternetConnection = new System.Threading.Timer((object obj) =>
      {
        IsOnline = CheckImOnline();
        if (IsOnline)
        {
          CheckInternetConnection.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
          RunningCheckInternetConnection = 0;
          foreach (var Network in Networks)
          {

          }
        }
      }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

      private static int RunningCheckInternetConnection = 0;
      /// <summary>
      /// He waits and checks the internet connection, and starts the communication protocol by notifying the online presence
      /// </summary>
      internal static void WaitForInternetConnection()
      {
        RunningCheckInternetConnection += 1;
        if (RunningCheckInternetConnection == 1)
          CheckInternetConnection.Change(0, 30000);
      }
    }
  }
}
