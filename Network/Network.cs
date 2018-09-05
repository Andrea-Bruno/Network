using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Serialization;
using System.Linq;

namespace NetworkManager
{
  public static class Network
  {
    public static void Initialize(Node[] EntryPoints, string NetworkName = "testnet", string MyAddress = null)
    {
      Setup.Network.NetworkName = NetworkName;
      NodeList = EntryPoints;
      var Nodes = GetNetworkNodes();
      if (Nodes != null)
      {
        var List = new List<Node>(Nodes);
        if (MyAddress != null)
        {
          MyNode = new Node { MachineName = Setup.Network.MachineName, Server = MyAddress };
          List.Add(MyNode);
        }
        NodeList = List.OrderBy(o => o.Server).ToArray();
      }
    }
    //private static string _NetworkName = "testnet";
    public static string NetworkName { get { return Setup.Network.NetworkName; } }
    private static bool _IsOnline;
    public static bool IsOnline { get { return _IsOnline; } }
    private static Node[] NodeList;
    private static Node MyNode;
    static class MappingNetwork
    {
      static List<List<Node>> NetworkGroups = new List<List<Node>>();//All groups of the network
      static List<List<Node>> MyGroups = new List<List<Node>>();//Only my groups
      static List<NextNode> NextNodes = new List<NextNode>();//Communication nodes
      private static void SetNodeGroups(Node[] NodeList)
      {
        var Nodes = new List<Node>(NodeList);
        lock (NodeList)
        {
          List<List<Node>> Groups = null;
          do
          {
            if (Nodes == null)
              Nodes = HorizontalNodes(Groups);
            Groups = Regroup(Nodes);
            NetworkGroups.AddRange(Groups);
            Nodes = null;
          } while (Groups.Count > 1);
        }
        foreach (var item in NetworkGroups)
          if (item.Contains(MyNode))
          {
            MyGroups.Add(item);
            if (item.Count > 1)
            {
              var NextNode = new NextNode();
              NextNodes.Add(NextNode);
              NextNode.GroupId = NetworkGroups.IndexOf(item);
              if (item.Last() == MyNode)
                NextNode.Node = item.First();
              else
                NextNode.Node = item[item.IndexOf(MyNode) + 1];
            }
          }
      }
      class NextNode
      {
        public int GroupId;
        public Node Node;
      }

      //private static List<List<Node>> MappingGroup()
      //{
      //  var Groups = new List<List<Node>>();
      //  var Group = new List<Node>();
      //  foreach (var Node in NodeList)
      //  {
      //    if (Group.Count == 0)
      //      Groups.Add(Group);
      //    Group.Add(Node);
      //    if (Group.Count == 20)
      //      Group = new List<Node>();
      //  }
      //  return Groups;
      //}

      private static List<Node> HorizontalNodes(List<List<Node>> NodeGroups)
      {
        var NodeList = new List<Node>();
        foreach (var item in NodeGroups)
          NodeList.Add(item[0]);
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
          if (Group.Count == 20)
            Group = new List<Node>();
        }
        return Groups;
      }
    }

    public static bool ReceivesHttpRequest(Dictionary<string, string> QueryString, Dictionary<string, string> Form, out string ContentType, System.IO.Stream OutputStream)
    {
      //Setting = CurrentSetting();
      //Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);

      QueryString.TryGetValue("network", out string NetworkName);
      if (Setup.Network.NetworkName == NetworkName)
      {
        QueryString.TryGetValue("app", out string AppName);
        QueryString.TryGetValue("touser", out string ToUser);
        QueryString.TryGetValue("fromuser", out string FromUser);
        QueryString.TryGetValue("post", out string Post);
        QueryString.TryGetValue("request", out string Request);
        int SecTimeout;
        if (QueryString.ContainsKey("sectimeout"))
          SecTimeout = int.Parse(QueryString["sectimeout"]);
        int SecWaitAnswer;
        if (QueryString.ContainsKey("secwaitanswer"))
          SecWaitAnswer = int.Parse(QueryString["secwaitanswer"]);

        string XmlObject = null;
        if (Form.ContainsKey("object"))
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
              if (!string.IsNullOrEmpty(Post))
              {
                //Is a object trasmission
                if (Post == "VectorBlocks")
                {
                  if (string.IsNullOrEmpty(FromUser))
                    ReturnObject = "error: no server name setting";
                  else
                  {
                    if (PostActions.ContainsKey(Post))
                    {
                      ReturnObject = PostActions[Post](XmlObject);
                    }
                  }
                }
              }
              if (!string.IsNullOrEmpty(Request))
              {
                //Is a request o object
                if (Request == "NetworkNodes")
                  ReturnObject = NodeList;
                ContentType = "text/xml;charset=utf-8";
                XmlSerializer xml = new XmlSerializer(ReturnObject.GetType());
                XmlSerializerNamespaces xmlns = new XmlSerializerNamespaces();
                xmlns.Add(string.Empty, string.Empty);
                xml.Serialize(OutputStream, ReturnObject, xmlns);
                return true;
              }
            }
          }
          catch (Exception ex)
          {
            ReturnObject = ex.Message;
          }
          if (ReturnObject != null && string.IsNullOrEmpty(Request))
          {
            ObjectVector Vector = new ObjectVector(ToUser, ReturnObject);
            ContentType = "text/xml;charset=utf-8";
            XmlSerializer xml = new XmlSerializer(typeof(ObjectVector));
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
    public delegate object GetObject(string XmlObject);
    private static Dictionary<string, GetObject> PostActions = new Dictionary<string, GetObject>();
    public static bool AddPostAction(string ActionName, GetObject GetObject)
    {
      lock (PostActions)
      {
        if (PostActions.ContainsKey(ActionName))
          return false;
        else
          PostActions.Add(ActionName, GetObject);
      }
      return true;
    }
    public static string SendObjectSync(object Obj, string WebAddress = null, System.Collections.Specialized.NameValueCollection Dictionary = null, string ToUser = null, int SecTimeOut = 0, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
    {
      var Reader = ExecuteServerRequest(false, WebAddress, null, Obj, Dictionary, null, SecWaitAnswer, ExecuteIfNoAnswer, SecTimeOut, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
      return Reader.HTML;
    }
    public static WebReader SendObjectAsync(ref object Obj, string WebAddress = null, System.Collections.Specialized.NameValueCollection Dictionary = null, string ToUser = null, int SecTimeOut = 0, int SecWaitAnswer = 0, OnReceivedObject ExecuteOnReceivedObject = null, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
    {
      return ExecuteServerRequest(true, WebAddress, null, Obj, Dictionary, ExecuteOnReceivedObject, SecWaitAnswer, ExecuteIfNoAnswer, SecTimeOut, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
    }
    public static string GetObjectSync(string WebAddress = null, string Request = null, string ToUser = null, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
    {
      var Reader = ExecuteServerRequest(false, WebAddress, Request, null, null, null, SecWaitAnswer, ExecuteIfNoAnswer, 0, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
      return Reader.HTML;
    }
    public static WebReader GetObjectAsync(OnReceivedObject ExecuteOnReceivedObject, string WebAddress = null, string Request = null, string ToUser = null, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
    {
      return ExecuteServerRequest(true, WebAddress, Request, null, null, ExecuteOnReceivedObject, SecWaitAnswer, ExecuteIfNoAnswer, 0, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
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
          object ReturmObj;
          Converter.XmlToObject(Html, typeof(ObjectVector), out ReturmObj);
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
        if (Async)
          WebClient.OpenReadAsync(new Uri(Url));
        else
        {
          if (Dictionary == null)
            Dictionary = new System.Collections.Specialized.NameValueCollection();
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
    public class Node
    {
      public string Server;
      public string MachineName;
      public string PublicKey;
    }
    public enum Request { NetworkNodes }
    private static string SendRequest(Request Request)
    {
      int Try = 0;
      string XmlResult;
      do
      {
        Try += 1;
        var Node = GetRandomNode();
        //XmlResult = SendObjectSync(null, Node.Server, null, Node.MachineName + ".");
        XmlResult = GetObjectSync(Node.Server, Request.NetworkNodes.ToString(), Node.MachineName + ".");
      } while (string.IsNullOrEmpty(XmlResult) && Try <= 10);
      if (Try > 10)
        _IsOnline = false;
      else
        _IsOnline = true;
      return XmlResult;
    }
    private static Node[] GetNetworkNodes()
    {
      var XmlResult = SendRequest(Request.NetworkNodes);
      if (string.IsNullOrEmpty(XmlResult))
        return null;
      Converter.XmlToObject(XmlResult, typeof(Node[]), out object ReturmObj);
      return (Node[])ReturmObj;
    }
    private static Node GetRandomNode()
    {
      lock (NodeList)
      {
        if (NodeList.Length > 0)
          return NodeList[new Random().Next(NodeList.Length)];
      }
      return null;
    }
    public enum Message { ImOnline, ImOfflone }
    private static string SendMessage(Message Message)
    {
      int Try = 0;
      string XmlResult;
      do
      {
        Try += 1;
        var Node = GetRandomNode();
        //XmlResult = SendObjectSync(null, Node.Server, null, Node.MachineName + ".");
        XmlResult = SendObjectSync(Message, Node.Server, null, Node.MachineName + ".");
      } while (string.IsNullOrEmpty(XmlResult) && Try <= 10);
      if (Try > 10)
        _IsOnline = false;
      else
        _IsOnline = true;
      return XmlResult;
    }

  }

}
