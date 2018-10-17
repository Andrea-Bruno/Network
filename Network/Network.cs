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
    /// This procedure receives an http request and processes the response based on the input received and the protocol
    /// </summary>
    /// <param name="QueryString">QueryString Collection</param>
    /// <param name="Form">Form Collection</param>
    /// <param name="FromIP">the IP of who generated the request</param>
    /// <param name="ContentType">The ContentType of the answer</param>
    /// <param name="OutputStream">The stream to which the reply will be sent</param>
    /// <param name="MyIP">This parameter is used only if you are using virtual devices and you want to direct the request to a specific device. This parameter is used by developers to make simulations using a virtual p2p network</param>
    /// <returns></returns>
    public static bool OnReceivesHttpRequest(System.Collections.Specialized.NameValueCollection QueryString, System.Collections.Specialized.NameValueCollection Form, string FromIP, out string ContentType, System.IO.Stream OutputStream, uint MyIP = 0)
    {
      BaseDevice Device = null;
      if (BaseDevices.Count == 1)
        Device = BaseDevices[0];
      else
        Device = BaseDevices.Find(x => x.VirtualDevice.IP == MyIP);
      ContentType = null;
      if (Device != null)
      {
        var NetworkName = QueryString["network"];
        foreach (var Network in Device.Networks)
        {
          if (NetworkName == Network.NetworkName)
            if (Network._OnReceivesHttpRequest(QueryString, Form, FromIP, out ContentType, OutputStream))
              return true;
        }
      }
      return false;
    }
    private bool _OnReceivesHttpRequest(System.Collections.Specialized.NameValueCollection QueryString, System.Collections.Specialized.NameValueCollection Form, string FromIP, out string ContentType, System.IO.Stream OutputStream)
    {
      //ContentType = null;
      //var NetworkName = QueryString["network"];

      //foreach (var Network in Networks)
      //{
      //  if (NetworkName == Network.NetworkName)
      //    if (Network._OnReceivesHttpRequest(QueryString, Form, FromIP, out ContentType, OutputStream))
      //      return true;


      //}
      //return false;

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

  public class Device
  {
    /// <summary>
    /// Returns the list of virtual devices already initialized, and the real device if already initialized
    /// </summary>
    /// <param name="VirtualDevice"></param>
    public Device(VirtualDevice VirtualDevice)
    {
      OnlineDetection = new OnlineDetectionClass(this);
      this.VirtualDevice = VirtualDevice;
      if (VirtualDevice != null)
      {
        BD = BaseDevices.Find(x => x.VirtualDevice == VirtualDevice);
        if (BD == null)
          BD = new BaseDevice();
      }
      else
      {
        if (RealDevice == null)
          RealDevice = new BaseDevice();
        BD = RealDevice;
      }
      if (!BaseDevices.Contains(BD))
        BaseDevices.Add(BD);
    }
    public static List<VirtualDevice> Devices
    {
      get
      {
        var List = new List<VirtualDevice>();
        foreach (var BaseDevice in BaseDevices)
        {
          List.Add(BaseDevice.VirtualDevice);
        }
        return List;
      }
    }
    internal static List<BaseDevice> BaseDevices = new List<BaseDevice>();
    private readonly BaseDevice BD;
    internal List<Network> Networks { get { return BD.Networks; } }
    public string MachineName { get { return BD.MachineName; } }
    internal bool _IsOnline { set { BD._IsOnline = value; } }
    internal bool IsOnline { get { return BD.IsOnline; } }
    internal DateTime Now { get { return BD.Now(); } }
    internal VirtualDevice VirtualDevice { get { return BD.VirtualDevice; } set { BD.VirtualDevice = value; } }
    private static BaseDevice RealDevice;
    internal class BaseDevice
    {
      public List<Network> Networks = new List<Network>();
      public string MachineName
      {
        get
        {
          if (VirtualDevice == null)
            return VirtualDevice.MachineName;
          else
            return Environment.MachineName;
        }
      }
      internal bool _IsOnline;
      public bool IsOnline { get { return _IsOnline; } }
      internal DateTime Now()
      {
        return DateTime.UtcNow;
      }
      internal VirtualDevice VirtualDevice;
    }
    internal readonly OnlineDetectionClass OnlineDetection;
    internal class OnlineDetectionClass
    {
      public OnlineDetectionClass(Device Device)
      {
        CheckInternetConnection = new System.Timers.Timer(30000) { AutoReset = true, Enabled = false };
        CheckInternetConnection.Elapsed += (sender, e) => CheckInternet();
        this.Device = Device;
      }
      private readonly Device Device;
      private bool CheckImOnline()
      {
        if (Device.BD.VirtualDevice != null)
          return Device.IsOnline;
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
      private System.Timers.Timer CheckInternetConnection;
      private void CheckInternet()
      {
        Device.BD._IsOnline = CheckImOnline();
        if (Device.BD._IsOnline)
        {
          CheckInternetConnection.Stop();
          RunningCheckInternetConnection = 0;
        }
      }

      private int RunningCheckInternetConnection = 0;
      /// <summary>
      /// He waits and checks the internet connection, and starts the communication protocol by notifying the online presence
      /// </summary>
      internal void WaitForInternetConnection()
      {
        RunningCheckInternetConnection += 1;
        if (RunningCheckInternetConnection == 1)
          CheckInternetConnection.Start();
      }
    }
  }

  public class VirtualDevice
  {
    public VirtualDevice() { IP = LastIp + 1; LastIp = IP; }
    private static uint LastIp = 0;
    public string MachineName = "VirtualDevice";
    /// <summary>
    /// Something like that "sim://xxxxxxx"
    /// </summary>
    public string Address;
    public uint IP;
    public void SetIP(string IP)
    {
      this.IP = Converter.IpToUint(IP);
    }
  }
}
