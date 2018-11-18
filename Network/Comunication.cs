using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Web;
using System.Xml.Serialization;
using Timer = System.Timers.Timer;

namespace NetworkManager
{
  /// <summary>
  ///   It creates communication mechanisms that can be used by a protocol to communicate between nodes via the Internet
  /// </summary>
  public class Communication
  {
    public delegate void OnReceivedObject(string fromUser, string objectName, string xmlObject);

    private readonly string _appName = Assembly.GetExecutingAssembly().FullName.Split(',')[0];
    private readonly string _masterServer;
    private readonly string _masterServerMachineName;
    private readonly NetworkConnection _networkConnection;

    public Communication(NetworkConnection networkConnection, string masterServerMachineName = null, string masterServer = null)
    {
      _networkConnection = networkConnection;
      _masterServerMachineName = masterServerMachineName;
      _masterServer = masterServer;
    }

    public string SendObjectSync(object obj, string webAddress = null, NameValueCollection form = null,
      string toUser = null, int secTimeOut = 0, int secWaitAnswer = 0, Action executeIfNoAnswer = null,
      bool cancelAllMyRequest = false, bool removeObjectsToMe = false, bool removeMyObjects = false)
    {
      var reader = ExecuteServerRequest(false, webAddress, null, obj, form, null, secWaitAnswer, executeIfNoAnswer,
        secTimeOut, toUser, cancelAllMyRequest, removeObjectsToMe, removeMyObjects);
      return reader.Html;
    }

    public BaseWebReader SendObjectAsync(ref object obj, string webAddress = null, NameValueCollection form = null,
      string toUser = null, int secTimeOut = 0, int secWaitAnswer = 0, OnReceivedObject executeOnReceivedObject = null,
      Action executeIfNoAnswer = null, bool cancelAllMyRequest = false, bool removeObjectsToMe = false,
      bool removeMyObjects = false)
    {
      return ExecuteServerRequest(true, webAddress, null, obj, form, executeOnReceivedObject, secWaitAnswer,
        executeIfNoAnswer, secTimeOut, toUser, cancelAllMyRequest, removeObjectsToMe, removeMyObjects);
    }

    public string GetObjectSync(string webAddress = null, string request = null, object obj = null,
      string toUser = null, int secWaitAnswer = 0, Action executeIfNoAnswer = null, bool cancelAllMyRequest = false,
      bool removeObjectsToMe = false, bool removeMyObjects = false)
    {
      var reader = ExecuteServerRequest(false, webAddress, request, obj, null, null, secWaitAnswer, executeIfNoAnswer,
        0, toUser, cancelAllMyRequest, removeObjectsToMe, removeMyObjects);
      return reader.Html;
    }

    public BaseWebReader GetObjectAsync(OnReceivedObject executeOnReceivedObject, string webAddress = null,
      string request = null, object obj = null, string toUser = null, int secWaitAnswer = 0,
      Action executeIfNoAnswer = null, bool cancelAllMyRequest = false, bool removeObjectsToMe = false,
      bool removeMyObjects = false)
    {
      return ExecuteServerRequest(true, webAddress, request, obj, null, executeOnReceivedObject, secWaitAnswer,
        executeIfNoAnswer, 0, toUser, cancelAllMyRequest, removeObjectsToMe, removeMyObjects);
    }

    private string UrlServer()
    {
      return _masterServer == "" ? null : _masterServer.TrimEnd('/');
    }

    private BaseWebReader ExecuteServerRequest(bool async, string webAddress = null, string request = null, object obj = null, NameValueCollection form = null, OnReceivedObject executeOnReceivedObject = null,
      int secWaitAnswer = 0, Action executeIfNoAnswer = null, int secTimeOut = 0, string toUser = null,
      bool cancelAllMyRequest = false, bool removeObjectsToMe = false, bool removeMyObjects = false)
    {
      if (webAddress == null)
        webAddress = UrlServer();
      webAddress = webAddress.TrimEnd('/');
      webAddress += "/?networkConnection=" + Uri.EscapeDataString(_networkConnection.NetworkName) + "&app=" + Uri.EscapeDataString(_appName) + "&fromUser=" + Uri.EscapeDataString(Environment.MachineName) + "&secWaitAnswer=" + secWaitAnswer;
      if (cancelAllMyRequest)
        webAddress += "&cancelRequest=true";
      if (removeObjectsToMe)
        webAddress += "&removeObjects=true";
      if (removeMyObjects)
        webAddress += "&removeMyObjects=true";
      if (string.IsNullOrEmpty(toUser))
        toUser = _masterServerMachineName + ".";
      if (!string.IsNullOrEmpty(toUser))
        webAddress += "&toUser=" + toUser;
      if (!string.IsNullOrEmpty(request))
        webAddress += "&request=" + request;

      Action<string> parser = null;
      if (executeOnReceivedObject != null)
        parser = html =>
        {
          Converter.XmlToObject(html, typeof(ObjectVector), out var returnObj);
          var objectVector = (ObjectVector)returnObj;
          if (objectVector != null)
            executeOnReceivedObject.Invoke(objectVector.FromUser, objectVector.ObjectName, objectVector.XmlObject);
        };
      else
        webAddress += "&noGetObject=true";

      if (obj != null)
      {
        webAddress += "&post=" + obj.GetType().Name + "&secTimeout=" + secTimeOut;
        var str = new StringWriter();
        var xml = new XmlSerializer(obj.GetType());
        var xmlns = new XmlSerializerNamespaces();
        xmlns.Add(string.Empty, string.Empty);
        xml.Serialize(str, obj, xmlns);
        var postData = str.ToString();
        if (form == null)
          form = new NameValueCollection();
        var strCod = HttpUtility.UrlEncode((Utility.MinifyXml(postData)));
        form.Add("object", strCod);
      }
      if (webAddress.StartsWith("vd://"))
        return VirtualReadWeb(async, _networkConnection.VirtualDevice, webAddress, parser, null, form, secWaitAnswer, executeIfNoAnswer);
      return ReadWeb(async, webAddress, parser, null, form, secWaitAnswer, executeIfNoAnswer);
      //var virtualDevice = Device.FindDeviceByAddress(webAddress);
      //return virtualDevice != null
      //  ? VirtualReadWeb(async, Converter.UintToIp(_networkConnection.VirtualDevice.Ip), webAddress, parser, null, form, secWaitAnswer, executeIfNoAnswer, virtualDevice) : (BaseWebReader)ReadWeb(async, webAddress, parser, null, form, secWaitAnswer, executeIfNoAnswer);
    }

    private static VirtualWebReader VirtualReadWeb(bool async, VirtualDevice client, string url, Action<string> parser, Action elapse, NameValueCollection form = null, int secTimeout = 0, Action executeAtTimeout = null)
    {
      return new VirtualWebReader(async, client, url, parser, elapse, form, secTimeout, executeAtTimeout);
    }

    private static WebReader ReadWeb(bool async, string url, Action<string> parser, Action elapse, NameValueCollection form = null, int secTimeout = 0, Action executeAtTimeout = null)
    {
      return new WebReader(async, url, parser, elapse, form, secTimeout, executeAtTimeout);
    }

    private class VirtualWebReader : BaseWebReader
    {
      private VirtualWebClient _virtualWebClient;
      public VirtualWebReader(bool async, VirtualDevice client, string url, Action<string> parser, Action elapse, NameValueCollection form = null, int secTimeout = 0, Action executeAtTimeout = null) : base(parser, elapse, form, secTimeout, executeAtTimeout)
      {
        VirtualWebClient = new VirtualWebClient();
        Cancel = () => { VirtualWebClient.CancelAsync(); };
        UploadAsync = () => { VirtualWebClient.UploadValuesAsync(url, "POST", form, client); };
        Upload = () =>
        {
          Html = VirtualWebClient.UploadValues(url, "POST", form, client);
          return Html;
        };
        Start(url, async);
      }

      private VirtualWebClient VirtualWebClient
      {
        [MethodImpl(MethodImplOptions.Synchronized)]
        get => _virtualWebClient;

        [MethodImpl(MethodImplOptions.Synchronized)]
        set
        {
          if (_virtualWebClient != null) _virtualWebClient.OpenReadCompleted -= WebClient_OpenReadCompleted;

          _virtualWebClient = value;
          if (_virtualWebClient != null) _virtualWebClient.OpenReadCompleted += WebClient_OpenReadCompleted;
        }
      }

      private void WebClient_OpenReadCompleted(object sender, EventArgs e)
      {
        var error = VirtualWebClient.Error != null;
        var canceled = false;
        if (error)
          canceled = VirtualWebClient.Canceled;
        var html = VirtualWebClient.Result;
        OpenReadCompleted(html, error, canceled);
      }
    }

    private class WebReader : BaseWebReader
    {
      private WebClient _webClient;

      public WebReader(bool async, string url, Action<string> parser, Action elapse, NameValueCollection form, int secTimeout = 0, Action executeAtTimeout = null) : base(parser, elapse, form, secTimeout,
        executeAtTimeout)
      {
        WebClient = new WebClient();
        Cancel = () => { WebClient.CancelAsync(); };
        UploadAsync = () => { WebClient.UploadValuesAsync(new Uri(url), "POST", form); };
        Upload = () =>
        {
          var responseBytes = WebClient.UploadValues(url, "POST", form);
          Html = new UTF8Encoding().GetString(responseBytes);
          return Html;
        };
        Start(url, async);
      }

      private WebClient WebClient
      {
        [MethodImpl(MethodImplOptions.Synchronized)]
        get => _webClient;

        [MethodImpl(MethodImplOptions.Synchronized)]
        set
        {
          if (_webClient != null) _webClient.OpenReadCompleted -= WebClient_OpenReadCompleted;

          _webClient = value;
          if (_webClient != null) _webClient.OpenReadCompleted += WebClient_OpenReadCompleted;
        }
      }

      private void WebClient_OpenReadCompleted(object sender, OpenReadCompletedEventArgs e)
      {
        var contentType = WebClient.ResponseHeaders?["Content-Type"];
        var error = e.Error != null;
        var cancelled = error && e.Cancelled;
        var result = e.Result;
        OpenReadCompleted(result, error, cancelled, contentType);
      }
    }

    public class BaseWebReader
    {
      private Timer _timeout;
      protected Action Cancel;
      private NameValueCollection _dictionary;
      private readonly Action _elapse;
      private readonly Action<string> _execute; // Parser = Sub(Html As String)
      private readonly Action _executeAtTimeout;
      public string Html;
      protected Func<string> Upload;
      protected Action UploadAsync;

      protected BaseWebReader(Action<string> parser, Action elapse, NameValueCollection dictionary = null, int secTimeout = 0, Action executeAtTimeout = null)
      {
        _execute = parser;
        _elapse = elapse;
        _executeAtTimeout = executeAtTimeout;
        _dictionary = dictionary;
        if (secTimeout == 0) return;
        Timeout = new Timer { Interval = TimeSpan.FromSeconds(secTimeout).TotalMilliseconds };
        Timeout.Start();
      }

      private Timer Timeout
      {
        [MethodImpl(MethodImplOptions.Synchronized)]
        get => _timeout;

        [MethodImpl(MethodImplOptions.Synchronized)]
        set
        {
          if (_timeout != null) _timeout.Elapsed -= Timeout_Tick;

          _timeout = value;
          if (_timeout != null) _timeout.Elapsed += Timeout_Tick;
        }
      }

      private void Timeout_Tick(object sender, EventArgs e)
      {
        Cancel();
        _executeAtTimeout?.Invoke();
      }

      public void CancelAsync()
      {
        Timeout?.Stop();
        Cancel();
      }

      protected void Start(string url, bool async)
      {
        if (_dictionary == null)
          _dictionary = new NameValueCollection();
        if (async)
        {
          UploadAsync();
        }
        else
        {
#if !DEBUG
          try
          {
#endif
          Html = Upload();
#if !DEBUG
          }
          catch (Exception ex)
          {
            System.Diagnostics.Debug.Print(ex.Message);
            System.Diagnostics.Debugger.Break();
          }
#endif
          if (_execute != null && Html != null)
            _execute(Html);
          _elapse?.Invoke();
        }
      }

      internal void OpenReadCompleted(string html, bool error, bool canceled)
      {
        if (error == false && canceled == false)
        {
          Html = html;
          if (_execute != null && html != null)
            _execute(html);
        }

        _elapse?.Invoke();
      }

      internal void OpenReadCompleted(Stream result, bool error, bool canceled, string contentType)
      {
        Timeout?.Stop();
        if (error == false && canceled == false)
        {
          var binaryStreamReader = new BinaryReader(result);
          var bytes = binaryStreamReader.ReadBytes(Convert.ToInt32(binaryStreamReader.BaseStream.Length));
          if (bytes.Length != 0)
          {
            Encoding encoding = null;
            if (contentType != "")
            {
              var parts = contentType.Split('=');
              if (parts.Length == 2)
                try
                {
                  encoding = Encoding.GetEncoding(parts[1]);
                }
                catch (Exception ex)
                {
                  Debug.Print(ex.Message);
                  Debugger.Break();
                }
            }

            if (encoding == null)
            {
              var row = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
              if (row != "")
                try
                {
                  var p1 = row.IndexOf("charset=", StringComparison.Ordinal) + 1;
                  if (p1 > 0)
                  {
                    if (row[p1 + 7] == '"')
                      p1 += 9;
                    else
                      p1 += 8;
                    var p2 = row.IndexOf("\"", p1, StringComparison.Ordinal);
                    if (p2 > 0)
                    {
                      var encodeStr = row.Substring(p1 - 1, p2 - p1);
                      try
                      {
                        encoding = Encoding
                          .GetEncoding(
                            encodeStr); // http://msdn.microsoft.com/library/vstudio/system.text.encoding(v=vs.100).aspx
                      }
                      catch (Exception ex)
                      {
                        Debug.Print(ex.Message);
                        Debugger.Break();
                      }
                    }
                  }
                }
                catch (Exception ex)
                {
                  Debug.Print(ex.Message);
                  Debugger.Break();
                }
            }

            if (encoding != null)
            {
              Html = encoding.GetString(bytes, 0, bytes.Length);
              if (_execute != null && Html != null)
                _execute(Html);
            }
          }
        }

        _elapse?.Invoke();
      }
    }

    private class VirtualWebClient
    {
      private string _result;
      public bool Canceled;
      private int _currentWebRequest;
      public string Error;
      public NameValueCollection ResponseHeaders { get; private set; }
      public string Result
      {
        get => _result;
        private set
        {
          _result = value;
          OpenReadCompleted?.Invoke(this, null);
        }
      }
      public void CancelAsync()
      {
      }
      public void UploadValuesAsync(string address, string method, NameValueCollection form, VirtualDevice client)
      {
        new Thread(() => { Result = UploadValues(address, method, form, client); }).Start();
      }

      public string UploadValues(string address, string method, NameValueCollection form, VirtualDevice client)
      {
        //note: method is auto detected about this is not used
        Error = null;
        Canceled = false;
        var time = DateTime.Now.ToUniversalTime();
        _currentWebRequest += 1;
        var request = new WebRequest(address, form ?? new NameValueCollection(), method, Converter.UintToIp(client.Ip));
        var response = VirtualDevice.HttpRequest(request);
        Result = response?.Text;
        ResponseHeaders = response?.Headers;
        var mb = response?.Text?.Length / 1048576f ?? 0;
        // It is empirical but excellent for simulating the networkConnection speed as set by the Virtual Device
        var pauseMs = (int)(mb / client.NetSpeed * 1000 * _currentWebRequest);
        var msFromTime = (int)(DateTime.Now.ToUniversalTime() - time).TotalMilliseconds;
        pauseMs -= msFromTime;
        if (pauseMs > 0)
          Thread.Sleep(pauseMs);
        _currentWebRequest -= 1;
        return Result;
      }
      public event EventHandler OpenReadCompleted;
    }

    public class ObjectVector
    {
      public readonly string FromUser;
      public readonly string ObjectName;
      public readonly string XmlObject;

      public ObjectVector()
      {
      }

      public ObjectVector(string fromUser, string objectName, string xmlObject)
      {
        FromUser = fromUser;
        ObjectName = objectName;
        XmlObject = Utility.MinifyXml(xmlObject);
      }

      public ObjectVector(string fromUser, object obj)
      {
        FromUser = fromUser;
        ObjectName = obj.GetType().Name;

        var xmlSerializer = new XmlSerializer(obj.GetType());

        using (var textWriter = new StringWriter())
        {
          xmlSerializer.Serialize(textWriter, obj);
          XmlObject = Utility.MinifyXml(textWriter.ToString());
        }
      }
    }
  }
}