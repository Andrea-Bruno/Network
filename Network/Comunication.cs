using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace NetworkManager
{
  /// <summary>
  /// It creates communication mechanisms that can be used by a protocol to communicate between nodes via the Internet
  /// </summary>
  public class Comunication
  {
    public Comunication(Network Network, string MasterServerMachineName)
    {
      this.Network = Network;
      this.MasterServerMachineName = MasterServerMachineName;
    }
    private Network Network;
    private string MasterServerMachineName;
    public string SendObjectSync(object Obj, string WebAddress = null, NameValueCollection Form = null, string ToUser = null, int SecTimeOut = 0, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
    {
      var Reader = ExecuteServerRequest(false, WebAddress, null, Obj, Form, null, SecWaitAnswer, ExecuteIfNoAnswer, SecTimeOut, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
      return Reader.HTML;
    }
    public BaseWebReader SendObjectAsync(ref object Obj, string WebAddress = null, NameValueCollection Form = null, string ToUser = null, int SecTimeOut = 0, int SecWaitAnswer = 0, OnReceivedObject ExecuteOnReceivedObject = null, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
    {
      return ExecuteServerRequest(true, WebAddress, null, Obj, Form, ExecuteOnReceivedObject, SecWaitAnswer, ExecuteIfNoAnswer, SecTimeOut, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
    }
    public string GetObjectSync(string WebAddress = null, string Request = null, object Obj = null, string ToUser = null, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
    {
      var Reader = ExecuteServerRequest(false, WebAddress, Request, Obj, null, null, SecWaitAnswer, ExecuteIfNoAnswer, 0, ToUser, CancellAllMyRequest, RemoveObjectsToMe, RemoveMyObjects);
      return Reader.HTML;
    }
    public BaseWebReader GetObjectAsync(OnReceivedObject ExecuteOnReceivedObject, string WebAddress = null, string Request = null, object Obj = null, string ToUser = null, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
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
    private BaseWebReader ExecuteServerRequest(bool Async, string WebAddress = null, string Request = null, object Obj = null, NameValueCollection Form = null, OnReceivedObject ExecuteOnReceivedObject = null, int SecWaitAnswer = 0, Action ExecuteIfNoAnswer = null, int SecTimeOut = 0, string ToUser = null, bool CancellAllMyRequest = false, bool RemoveObjectsToMe = false, bool RemoveMyObjects = false)
    {
      if (WebAddress == null)
        WebAddress = UrlServer();
      WebAddress = WebAddress.TrimEnd('/');
      WebAddress += "?network=" + System.Uri.EscapeDataString(Network.NetworkName) + "&app=" + System.Uri.EscapeDataString(AppName) + "&fromuser=" + System.Uri.EscapeDataString(Environment.MachineName) + "&secwaitanswer=" + SecWaitAnswer.ToString();
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
        if (Form == null)
          Form = new NameValueCollection();
        string StrCod = Converter.StringToBase64(postData);
        Form.Add("object", StrCod);
      }
      var VirtualDevice = Device.FindDeviceByAddress(WebAddress);
      if (VirtualDevice != null)
        return VirtualReadWeb(Async, Converter.UintToIp(Network.VirtualDevice.IP), WebAddress, Parser, null, Form, SecWaitAnswer, ExecuteIfNoAnswer, VirtualDevice);
      else
        return ReadWeb(Async, WebAddress, Parser, null, Form, SecWaitAnswer, ExecuteIfNoAnswer);
    }
    public static VirtualWebReader VirtualReadWeb(bool Async, string MyIp, string Url, Action<string> Parser, Action Elapse, NameValueCollection Form = null, int SecTimeout = 0, Action ExecuteAtTimeout = null, VirtualDevice SendRequestTo = null)
    {
      return new VirtualWebReader(Async, MyIp, Url, Parser, Elapse, Form, SecTimeout, ExecuteAtTimeout, SendRequestTo);
    }
    public static WebReader ReadWeb(bool Async, string Url, Action<string> Parser, Action Elapse, NameValueCollection Form = null, int SecTimeout = 0, Action ExecuteAtTimeout = null)
    {
      return new WebReader(Async, Url, Parser, Elapse, Form, SecTimeout, ExecuteAtTimeout);
    }
    public class VirtualWebReader : BaseWebReader
    {
      public VirtualWebReader(bool Async, string MyIp, string Url, Action<string> Parser, Action Elapse, NameValueCollection Form = null, int SecTimeout = 0, Action ExecuteAtTimeout = null, VirtualDevice SendRequestTo = null) : base(Async, Url, Parser, Elapse, Form, SecTimeout, ExecuteAtTimeout)
      {
        VirtualDevice = SendRequestTo ?? Device.FindDeviceByAddress(Url);
        VirtualWebClient = new VirtualWebClient();
        Cancel = new Action(() => { VirtualWebClient.CancelAsync(); });
        UploadAsync = new Action(() => { VirtualWebClient.UploadValuesAsync(Url, "POST", Form, VirtualDevice, MyIp); });
        Upload = () =>
        {
          HTML = VirtualWebClient.UploadValues(Url, "POST", Form, VirtualDevice, MyIp);
          return HTML;
        };
        Start(Url, Async);
      }
      private VirtualDevice VirtualDevice;
      private VirtualWebClient _VirtualWebClient;
      private VirtualWebClient VirtualWebClient
      {
        [MethodImpl(MethodImplOptions.Synchronized)]
        get
        {
          return _VirtualWebClient;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        set
        {
          if (_VirtualWebClient != null)
          {
            _VirtualWebClient.OpenReadCompleted -= WebClient_OpenReadCompleted;
          }

          _VirtualWebClient = value;
          if (_VirtualWebClient != null)
          {
            _VirtualWebClient.OpenReadCompleted += WebClient_OpenReadCompleted;
          }
        }
      }
      private void WebClient_OpenReadCompleted(object sender, EventArgs e)
      {
        var Error = VirtualWebClient.Error != null;
        bool Cancelled = false;
        if (Error)
          Cancelled = VirtualWebClient.Cancelled;
        var HTML = VirtualWebClient.Result;
        OpenReadCompleted(HTML, Error, Cancelled);
      }
    }
    public class WebReader : BaseWebReader
    {
      public WebReader(bool Async, string Url, Action<string> Parser, Action Elapse, NameValueCollection Form = null, int SecTimeout = 0, Action ExecuteAtTimeout = null) : base(Async, Url, Parser, Elapse, Form, SecTimeout, ExecuteAtTimeout)
      {
        WebClient = new System.Net.WebClient();
        Cancel = new Action(() => { WebClient.CancelAsync(); });
        UploadAsync = new Action(() => { WebClient.UploadValuesAsync(new Uri(Url), "POST", Form); });
        Upload = () =>
        {
          var responsebytes = WebClient.UploadValues(Url, "POST", Form);
          HTML = (new System.Text.UTF8Encoding()).GetString(responsebytes);
          return HTML;
        };
        Start(Url, Async);
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
      private void WebClient_OpenReadCompleted(object sender, System.Net.OpenReadCompletedEventArgs e)
      {
        var ContentType = WebClient.ResponseHeaders?["Content-Type"];
        var Error = e.Error != null;
        bool Cancelled = false;
        if (Error)
          Cancelled = e.Cancelled;
        var Result = e.Result;
        OpenReadCompleted(Result, Error, Cancelled, ContentType);
      }
    }
    public class BaseWebReader
    {
      public BaseWebReader(bool Async, string Url, Action<string> Parser, Action Elapse, NameValueCollection Dictionary = null, int SecTimeout = 0, Action ExecuteAtTimeout = null)
      {
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
      }
      private NameValueCollection Dictionary;
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
        Cancel();
        if (ExecuteAtTimeout != null)
          ExecuteAtTimeout.Invoke();
      }
      public void CancelAsync()
      {
        if (Timeout != null)
          Timeout.Stop();
        Cancel();
      }
      protected Action Cancel;
      protected Action UploadAsync;
      protected Func<string> Upload;
      protected void Start(string Url, bool Async)
      {
        if (Dictionary == null)
          Dictionary = new NameValueCollection();
        if (Async)
          UploadAsync();
        else
        {
          try
          {
            HTML = Upload();
          }
          catch (Exception ex)
          {
            System.Diagnostics.Debug.Print(ex.Message);
            System.Diagnostics.Debugger.Break();
          }
          if (Execute != null && HTML != null)
            Execute(HTML);
          Elapse?.Invoke();
        }
      }
      public string HTML;
      internal void OpenReadCompleted(string HTML, bool Error, bool Cancelled)
      {
        if (Error == false && Cancelled == false)
        {
          this.HTML = HTML;
          if (Execute != null && HTML != null)
            Execute(HTML);
        }
        Elapse?.Invoke();
      }
      internal void OpenReadCompleted(Stream Result, bool Error, bool Cancelled, string ContentType)
      {
        if (Timeout != null)
          Timeout.Stop();
        if (Error == false && Cancelled == false)
        {
          System.IO.BinaryReader BinaryStreamReader = new System.IO.BinaryReader(Result);
          byte[] Bytes;
          Bytes = BinaryStreamReader.ReadBytes(System.Convert.ToInt32(BinaryStreamReader.BaseStream.Length));
          if (Bytes != null)
          {
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
                  System.Diagnostics.Debug.Print(ex.Message);
                  System.Diagnostics.Debugger.Break();
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
                        System.Diagnostics.Debug.Print(ex.Message);
                        System.Diagnostics.Debugger.Break();
                      }
                    }
                  }
                }
                catch (Exception ex)
                {
                  System.Diagnostics.Debug.Print(ex.Message);
                  System.Diagnostics.Debugger.Break();
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
        Elapse?.Invoke();
      }
    }
    public class VirtualWebClient
    {
      public string Error;
      public bool Cancelled;
      private string _Result;
      public string Result { get { return _Result; } set { _Result = value; OpenReadCompleted(this, null); } }
      public void CancelAsync()
      {

      }
      public void UploadValuesAsync(string address, string method, NameValueCollection Form, VirtualDevice VirtualDevice, string MyIp)
      {
        new System.Threading.Thread(() =>
        {
          Result = UploadValues(address, method, Form, VirtualDevice, MyIp);
        }).Start();
      }
      public string UploadValues(string address, string method, NameValueCollection Form, VirtualDevice VirtualDevice, string MyIp)
      {
        Error = null;
        Cancelled = false;
        if (VirtualDevice != null)
        {
          Uri myUri = new Uri(address);
          var QueryString = System.Web.HttpUtility.ParseQueryString(myUri.Query);
          //var Stream = new MemoryStream();
          var Response = VirtualDevice.Device.WebServer(new Device.BaseDevice.WebRequest(QueryString, Form ?? new NameValueCollection(), MyIp));
          Result = Response?.Text;
          return Result;
        }
        return null;
      }
      public NameValueCollection ResponseHeaders;
      public event EventHandler OpenReadCompleted;
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
}
