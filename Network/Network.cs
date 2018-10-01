using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Serialization;

namespace NetworkManager
{
  public static class Network
  {
    /// <summary>
    /// This method initializes the network.
    /// You can join the network as a node, and contribute to decentralization, or hook yourself to the network as an external user.
    /// To create a node, set the MyAddress parameter with your web address.If MyAddress is not set then you are an external user.
    /// </summary>
    /// <param name="MyAddress">Your web address. If you do not want to create the node, omit this parameter</param>
    /// <param name="EntryPoints">The list of permanent access points nodes, to access the network. If null then the entry points will be those set in the NetworkManager.Setup</param>
    /// <param name="NetworkName">The name of the infrastructure. For tests we recommend using "testnet"</param>
    public static void Initialize(string MyAddress = null, Node[] EntryPoints = null, string NetworkName = "testnet")
    {
      Setup.Network.MyAddress = MyAddress;
      Setup.Network.NetworkName = NetworkName;
      if (EntryPoints != null)
        Setup.Network.EntryPoints = EntryPoints;
      OnlineDetection.WaitForInternetConnection();
    }
    //private static string _NetworkName = "testnet";
    public static string NetworkName { get { return Setup.Network.NetworkName; } }
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
    private static List<Node> NodeList;
    private static Node GetRandomNode()
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
    private static Node MyNode;

    /// <summary>
    /// Performs a specific code addressed to a randomly selected node.
    /// </summary>
    /// <param name="Execute">The instructions to be executed</param>
    public static bool InteractWithRandomNode(Execute Execute)
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
    /// The spooler contains the logic that allows synchronization of data on the peer2peer network
    /// </summary>
    private static class Spooler
    {
      //static private Dictionary<string, List<BufferManager.Element>> DataSendedMemory = new Dictionary<string, List<BufferManager.Element>>();
      static internal void AddDatas(List<BufferManager.Element> Datas)
      {
        BufferManager.AddLocal(Datas);
      }
      private static Dictionary<System.Threading.Thread, string> ThreadsIP = new Dictionary<System.Threading.Thread, string>();
      //private static List<System.Threading.Thread> ThreadList = new List<System.Threading.Thread>();
      internal static bool ThreadBlock(System.Threading.Thread Thread, string NodeIp)
      {
        if (NodeList.Find((x) => x.IP == NodeIp) != null)
        {
          Thread.Suspend();
          lock (ThreadsIP)
            ThreadsIP.Add(Thread, NodeIp);
          return true;
        }
        else
          return false;
      }
      static private Dictionary<System.Threading.Thread, List<BufferManager.Element>> DataToSend = new Dictionary<System.Threading.Thread, List<BufferManager.Element>>();
      static internal List<BufferManager.Element> GetElements()
      {
        var Elements = DataToSend[System.Threading.Thread.CurrentThread];
        lock (DataToSend)
          DataToSend.Remove(System.Threading.Thread.CurrentThread);
        return Elements;
      }
      internal static int Sec = MappingNetwork.GroupSize / 4;
      static private System.Threading.Timer Process = new System.Threading.Timer(TimerEvent, null, Sec, Sec);
      static private void TimerEvent(object x)
      {
        var ToRemove = new List<System.Threading.Thread>();
        lock (ThreadsIP)
        {
          foreach (var Corrispondence in ThreadsIP)
          {
            var IP = Corrispondence.Value;
            //if (DataSendedMemory.TryGetValue(IP, out List<BufferManager.Element> Sended) == false)
            //{
            //  Sended = new List<BufferManager.Element>();
            //  DataSendedMemory.Add(IP, Sended);
            //}
            var ReadyToSend = new List<BufferManager.Element>();
            lock (BufferManager.Buffer)
              foreach (var Data in BufferManager.Buffer)
              {
                if (!Data.SendedToIP.Contains(IP))
                {
                  Data.SendedToIP.Add(IP);
                  ReadyToSend.Add(Data);
                }
              }
            if (ReadyToSend.Count != 0)
            {
              var Thread = Corrispondence.Key;
              DataToSend.Add(Thread, ReadyToSend);
              ToRemove.Add(Thread);
            }
          }
          foreach (var Thread in ToRemove)
          {
            ThreadsIP.Remove(Thread);
            Thread.Resume();
          }
        }
      }
    }
    /// <summary>
    /// Contains the logic that establishes a mapping of the network and its subdivision to increase its performance.
    /// The network is divided at a logical level into many ring groups, with a recursive pyramidal structure
    /// </summary>
    private static class MappingNetwork
    {
      internal const int GroupSize = 20;
      static private int Levels;
      static public TimeSpan NetworkSyncTimeSpan;
      static List<List<Node>> NetworkGroups = new List<List<Node>>();//All groups of the network
      static List<List<Node>> MyGroups = new List<List<Node>>();//Only my groups
      static List<NextNode> NodeToConnect = new List<NextNode>();//Communication nodes

      internal static void SetNodeGroups(List<Node> NodeList)
      {
        Levels = 0;
        var Nodes = new List<Node>(NodeList);
        lock (NetworkGroups)
        {
          List<List<Node>> Groups = null;
          do
          {
            Levels += 1;
            if (Nodes == null)
              Nodes = HorizontalNodes(Groups);
            Groups = Regroup(Nodes);
            NetworkGroups.AddRange(Groups);
            Nodes = null;
          } while (Groups.Count > 1);
          NetworkSyncTimeSpan = TimeSpan.FromMilliseconds(Spooler.Sec * (Levels + 3));// +3 is a security margin
          //Find groups whit MyNode
          lock (MyGroups)
          {
            MyGroups.Clear();
            lock (NodeToConnect)
            {
              NodeToConnect.Clear();
              foreach (var item in NetworkGroups)
                if (item.Contains(MyNode))
                {
                  MyGroups.Add(item);
                  if (item.Count > 1)
                  {
                    var NextNode = new NextNode();
                    if (item.Last() == MyNode)
                      NextNode.Node = item.First();
                    else
                      NextNode.Node = item[item.IndexOf(MyNode) + 1];
                    if (NextNode.Node != MyNode)
                    {
                      NextNode.GroupId = NetworkGroups.IndexOf(item);
                      NodeToConnect.Add(NextNode);
                    }
                  }
                }
            }
          }
        }
      }
      class NextNode
      {
        public int GroupId;
        public Node Node;
      }
      private static void ConnectToNextNodes()
      {
        lock (NodeToConnect)
          foreach (var NextNode in NodeToConnect)
          {
            Protocol.ConnectToNode(NextNode.Node);
          }
      }
      private static List<Node> HorizontalNodes(List<List<Node>> NodeGroups)
      {
        var NodeList = new List<Node>();
        foreach (var item in NodeGroups)
          NodeList.Add(item[0]);
        foreach (var item in NodeGroups)
        {
          int id = GroupSize / 2;
          if (id >= item.Count)
            id = item.Count - 1;
          if (!NodeList.Contains(item[id]))
            NodeList.Add(item[id]);
        }
        return NodeList;
      }
      private static List<List<Node>> Regroup(List<Node> Nodes)
      {
        var Groups = new List<List<Node>>();
        var Group = new List<Node>();
        foreach (var Node in Nodes)
        {
          if (Group.Count == 0)
            Groups.Add(Group);
          Group.Add(Node);
          if (Group.Count == GroupSize)
            Group = new List<Node>();
        }
        return Groups;
      }
    }
    /// <summary>
    /// The buffer is a mechanism that allows you to distribute objects on the network by assigning a timestamp.
    /// The objects inserted in the buffer will exit from it on all the nodes following the chronological order of input(they come out sorted by timestamp).
    /// The data output from the buffar must be managed by actions that are programmed when the network is initialized.
    /// </summary>
    static public class BufferManager
    {
      /// <summary>
      /// Send an object to the network to be inserted in the shared buffer
      /// </summary>
      /// <param name="Object">Object to send</param>
      /// <returns></returns>
      static public bool AddToSaredBuffer(object Object)
      {
        return Protocol.AddToSharedBuffer(Object) == Protocol.StandardAnsware.Ok;
        //return InteractWithRandomNode((Node Node) =>
        // {
        // });
      }
      static private System.Threading.Timer Process = new System.Threading.Timer(ToDoEverySec, null, 1000, 1000);
      static private void ToDoEverySec(object x)
      {
        lock (Buffer)
        {
          var ToRemove = new List<ElementBeffer>();
          foreach (var item in Buffer)
          {
            if ((Now() - item.Timestamp) >= MappingNetwork.NetworkSyncTimeSpan)
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
      /// </summary>
      /// <param name="XmlObject">Serialized object im format xml</param>
      /// <returns></returns>
      static internal bool AddLocal(string XmlObject)
      {
        if (string.IsNullOrEmpty(XmlObject))
          return false;
        else
        {
          DateTime Timestamp = Network.Now();
          var Element = new Element() { Timestamp = Timestamp, XmlObject = XmlObject };
          lock (Buffer)
          {
            Buffer.Add((ElementBeffer)Element);
            SortBuffer();
          }
          return true;
        }
      }
      static internal void AddLocal(List<Element> Elements)
      {
        lock (Buffer)
        {
          var Count = Buffer.Count();
          foreach (var Element in Elements)
          {
            if (Buffer.Find((x) => x.Timestamp == Element.Timestamp && x.XmlObject == Element.XmlObject) == null)
              Buffer.Add((ElementBeffer)Element);
          }
          if (Count != Buffer.Count())
            SortBuffer();
        }
      }
      static void SortBuffer()
      {
        Buffer.OrderBy(x => x.XmlObject);//Used for the element whit same Timestamp
        Buffer.OrderBy(x => x.Timestamp);
      }
      static internal List<ElementBeffer> Buffer;
      internal class ElementBeffer : Element
      {
        public List<String> SendedToIP = new List<String>();
      }
      public class Element
      {
        public DateTime Timestamp;
        public string XmlObject;
      }
      public delegate void SyncData(string XmlObject, DateTime Timestamp);
      /// <summary>
      /// Add a action used to local sync the objects in the buffer
      /// </summary>
      /// <param name="Action">Action to execute for every object</param>
      /// <param name="ForObjectName">Indicates what kind of objects will be treated by this action</param>
      static public bool AddSyncDataAction(SyncData Action, string ForObjectName)
      {
        if (BufferCompletedAction.ContainsKey(ForObjectName))
          return false;
        BufferCompletedAction.Add(ForObjectName, Action);
        return true;
      }
      static private Dictionary<string, SyncData> BufferCompletedAction = new Dictionary<string, SyncData>();
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
      //Setting = CurrentSetting();
      //Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);

      var NetworkName = QueryString["network"];
      //QueryString.TryGetValue("network", out string NetworkName);

      if (Setup.Network.NetworkName == NetworkName)
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

        if (ToUser == Setup.Network.MachineName || ToUser.StartsWith(Setup.Network.MachineName + "."))
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

                if (Protocol.StandardRequest.TryParse(Request, out Protocol.StandardRequest Rq))
                {
                  if (Rq == Protocol.StandardRequest.NetworkNodes)
                    ReturnObject = NodeList;
                  else if (Rq == Protocol.StandardRequest.Connect)
                  {
                    if (Spooler.ThreadBlock(System.Threading.Thread.CurrentThread, FromIP) == true)
                    {
                      ReturnObject = Spooler.GetElements();
                    }
                    else
                      ReturnObject = Protocol.StandardAnsware.Declined;
                  }
                  else if (Rq == Protocol.StandardRequest.AddToBuffer)
                    if (Network.BufferManager.AddLocal(XmlObject) == true)
                      ReturnObject = Protocol.StandardAnsware.Ok;
                    else
                      ReturnObject = Protocol.StandardAnsware.Error;
                  else if (Rq == Protocol.StandardRequest.ImOffline || Rq == Protocol.StandardRequest.ImOnline)
                    if (Converter.XmlToObject(XmlObject, typeof(Node), out object ObjNode))
                    {
                      var Node = (Node)ObjNode;
                      ReturnObject = Protocol.StandardAnsware.Ok;
                      if (Rq == Protocol.StandardRequest.ImOnline)
                      {
                        Node.DetectIP();
                        if (Node.IP != "127.0.0.1" && NodeList.Select(x => x.IP == Node.IP) != null)
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
                  else if (Rq == Protocol.StandardRequest.TestSpeed)
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
            Comunication.ObjectVector Vector = new Comunication.ObjectVector(ToUser, ReturnObject);
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
      }
      ContentType = null;
      return false;
    }
    public static class Comunication
    {
      public static string SendObjectSync(object Obj, string WebAddress = null, System.Collections.Specialized.NameValueCollection Dictionary = null, string ToUser = null, int SecTimeOut = 0, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
      {
        var Reader = ExecuteServerRequest(false, WebAddress, null, Obj, Dictionary, null, SecWaitAnswer, ExecuteIfNoAnswer, SecTimeOut, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
        return Reader.HTML;
      }
      public static WebReader SendObjectAsync(ref object Obj, string WebAddress = null, System.Collections.Specialized.NameValueCollection Dictionary = null, string ToUser = null, int SecTimeOut = 0, int SecWaitAnswer = 0, OnReceivedObject ExecuteOnReceivedObject = null, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
      {
        return ExecuteServerRequest(true, WebAddress, null, Obj, Dictionary, ExecuteOnReceivedObject, SecWaitAnswer, ExecuteIfNoAnswer, SecTimeOut, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
      }
      public static string GetObjectSync(string WebAddress = null, string Request = null, object Obj = null, string ToUser = null, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
      {
        var Reader = ExecuteServerRequest(false, WebAddress, Request, Obj, null, null, SecWaitAnswer, ExecuteIfNoAnswer, 0, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
        return Reader.HTML;
      }
      public static WebReader GetObjectAsync(OnReceivedObject ExecuteOnReceivedObject, string WebAddress = null, string Request = null, object Obj = null, string ToUser = null, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
      {
        return ExecuteServerRequest(true, WebAddress, Request, Obj, null, ExecuteOnReceivedObject, SecWaitAnswer, ExecuteIfNoAnswer, 0, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
      }
      public delegate void OnReceivedObject(string FromUser, string ObjectName, string XmlObject);
      private static string AppName = System.Reflection.Assembly.GetExecutingAssembly().FullName.Split(',')[0];
      private static string UrlServer()
      {
        if (Setup.Network.MasterServer == "")
          return null;
        return Setup.Network.MasterServer.TrimEnd('/');
      }
      private static WebReader ExecuteServerRequest(bool Async, string WebAddress = null, string Request = null, object Obj = null, System.Collections.Specialized.NameValueCollection Dictionary = null, OnReceivedObject ExecuteOnReceivedObject = null, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, int SecTimeOut = 0, string ToUser = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
      {
        if (WebAddress == null)
          WebAddress = UrlServer();
        WebAddress = WebAddress.TrimEnd('/');
        WebAddress += "?network=" + System.Uri.EscapeDataString(Setup.Network.NetworkName) + "&app=" + System.Uri.EscapeDataString(AppName) + "&fromuser=" + System.Uri.EscapeDataString(Setup.Network.MachineName) + "&secwaitanswer=" + SecWaitAnswer.ToString();
        if (CancellAllMyRequest)
          WebAddress += "&cancellrequest=true";
        if (RemoveObjectsToMe)
          WebAddress += "&removeobjects=true";
        if (RemoveMyObjects)
          WebAddress += "&removemyobjects=true";
        if (string.IsNullOrEmpty(ToUser))
          ToUser = Setup.Network.MasterServerMachineName + ".";
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
    static public class Protocol
    {
      public delegate object GetObject(string XmlObject);
      internal static Dictionary<string, GetObject> OnReceivingObjectsActions = new Dictionary<string, GetObject>();
      public static bool AddOnReceivingObjectAction(string NameTypeObject, GetObject GetObject)
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
      internal static Dictionary<string, GetObject> OnRequestActions = new Dictionary<string, GetObject>();
      public static bool AddOnRequestAction(string ActionName, GetObject GetObject)
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
      private static string NotifyToNode(string Request, object Obj = null, Node ToNode = null)
      {
        int Try = 0;
        string XmlResult;
        do
        {
          Try += 1;
          if (ToNode == null)
            ToNode = GetRandomNode();
          if (ToNode == null)
            return "";
          XmlResult = Comunication.GetObjectSync(ToNode.Address, Request, Obj, ToNode.MachineName + ".");
        } while (string.IsNullOrEmpty(XmlResult) && Try <= 10);
        if (Try > 10)
          _IsOnline = false;
        else
          _IsOnline = true;
        return XmlResult;
      }
      private static string SendRequest(StandardRequest Request, object Obj = null, Node ToNode = null)
      {
        return NotifyToNode(Request.ToString(), Obj, ToNode);
      }
      internal enum StandardRequest { NetworkNodes, ImOnline, ImOffline, TestSpeed, AddToBuffer, Connect }
      public enum StandardAnsware { Ok, Error, DuplicateIP, TooSlow, Failure, NoAnsware, Declined }
      internal static List<Node> GetNetworkNodes()
      {
        var XmlResult = SendRequest(StandardRequest.NetworkNodes);
        if (string.IsNullOrEmpty(XmlResult))
          return new List<Node>();
        Converter.XmlToObject(XmlResult, typeof(List<Node>), out object ReturmObj);
        NodeList = (List<Node>)ReturmObj;
        lock (NodeList)
        {
          if (!String.IsNullOrEmpty(Setup.Network.MyAddress))
          {
            var Answare = Protocol.ImOnline(new Node { MachineName = Setup.Network.MachineName, Address = Setup.Network.MyAddress });
            // if Answare = NoAnsware then I'm the first online node in the network  
            NodeList.RemoveAll(x => x.Address == MyNode.Address);
            NodeList.Add(MyNode);
          }
          NodeList = NodeList.OrderBy(o => o.Address).ToList();
        }
        if (MyNode != null)
        {
          MappingNetwork.SetNodeGroups(NodeList);
        }
        return NodeList;
      }
      internal static StandardAnsware ImOffline()
      {
        var XmlResult = SendRequest(StandardRequest.ImOffline, MyNode);
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
      internal static StandardAnsware ImOnline(Node MyNode)
      {
        var XmlResult = SendRequest(StandardRequest.ImOnline, MyNode);
        MyNode.DetectIP();
        Network.MyNode = MyNode;
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
      internal static bool SpeedTest(Node NodeToTesting)
      {

        var Start = DateTime.UtcNow;
        for (int i = 0; i < 10; i++)
        {
          var XmlResult = SendRequest(StandardRequest.TestSpeed, null, NodeToTesting);
          if (XmlResult == null || XmlResult.Length != 1048616)
            return false;
        }
        var Speed = (DateTime.UtcNow - Start).TotalMilliseconds;
        return Speed <= 3000;
      }
      internal static StandardAnsware AddToSharedBuffer(Object Object)
      {
        try
        {
          var XmlResult = SendRequest(StandardRequest.AddToBuffer, Object);
          //var XmlResult = Comunication.SendObjectSync(Object, Node.Address, null, Node.MachineName);
          if (string.IsNullOrEmpty(XmlResult))
            return StandardAnsware.NoAnsware;
          else
          {
            object ReturmObj;
            Converter.XmlToObject(XmlResult, typeof(Protocol.StandardAnsware), out ReturmObj);
            StandardAnsware Answare = (StandardAnsware)ReturmObj;
            return Answare;
          }
        }
        catch (Exception)
        {
          return StandardAnsware.Error;
        }
      }
      private static Dictionary<Node, int> FailureList = new Dictionary<Node, int>();
      internal static void ConnectToNode(Node Node)
      {
        new System.Threading.Thread(() =>
        {
          var XmlResult = SendRequest(StandardRequest.Connect, null, Node);
          if (string.IsNullOrEmpty(XmlResult))
          {
            //the node is disconnected
            if (NodeList.Contains(Node))
            {
              //try reconnect
              if (OnlineDetection.CheckImOnline())
              {
                int Attempts;
                lock (FailureList)
                {
                  if (FailureList.TryGetValue(Node, out Attempts))
                  {
                    FailureList.Remove(Node);
                    Attempts += 1;
                    FailureList.Add(Node, Attempts);
                  }
                  else
                  {
                    Attempts = 1;
                    FailureList.Add(Node, 1);
                  }
                }
                if (Attempts < 3)
                  ConnectToNode(Node);
              }
              else
                OnlineDetection.WaitForInternetConnection();
            }
          }
          else
          {
            lock (FailureList)
              if (FailureList.ContainsKey(Node))
                FailureList.Remove(Node);
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
          Running = 0;
          if (Setup.Network.EntryPoints != null)
          {
            var EntryPointsList = Setup.Network.EntryPoints.ToList();
            EntryPointsList.RemoveAll(x => x.Address == Setup.Network.MyAddress);
            NodeList = EntryPointsList;
          }
          NodeList = Protocol.GetNetworkNodes();
        }
      }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
      private static int Running = 0;
      /// <summary>
      /// He waits and checks the internet connection, and starts the communication protocol by notifying the online presence
      /// </summary>
      internal static void WaitForInternetConnection()
      {
        Running += 1;
        if (Running == 1)
          CheckInternetConnection.Change(0, 30000);
      }
    }
  }
}
