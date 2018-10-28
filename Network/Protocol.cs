using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static NetworkManager.Network;

namespace NetworkManager
{
  public class Protocol
  {
    private Network Network;
    internal Protocol(Network Network)
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
      {
        System.Diagnostics.Debugger.Break();
        Network.IsOnline = false;
        Network.OnlineDetection.WaitForInternetConnection();
      }
      return XmlResult;
    }
    private string SendRequest(Node ToNode, StandardMessages Message, object Obj = null)
    {
      return NotifyToNode(ToNode, Message.ToString(), Obj);
    }
    internal enum StandardMessages { NetworkNodes, ImOnline, ImOffline, RequestTestSpeed, TestSpeed, AddToBuffer, SendElementsToNode, SendTimestampSignatureToNode, GetStats }
    public enum StandardAnsware { Ok, Disconnected, Error, DuplicateIP, TooSlow, Failure, NoAnsware, Declined, IpError }
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
        catch (Exception ex)
        {
          System.Diagnostics.Debug.Print(ex.Message);
          System.Diagnostics.Debugger.Break();
        }
      return StandardAnsware.Error;
    }
    internal StandardAnsware ImOnline(Node ToNode, Node MyNode)
    {
      var XmlResult = SendRequest(ToNode, StandardMessages.ImOnline, MyNode);
      if (string.IsNullOrEmpty(XmlResult))
        return StandardAnsware.NoAnsware;
      try
      {
        Converter.XmlToObject(XmlResult, typeof(StandardAnsware), out object Answare);
        return (StandardAnsware)Answare;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.Print(ex.Message);
        System.Diagnostics.Debugger.Break();
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
      Stats Stats = null;
      if (FromNode != null)
      {
        var XmlResult = SendRequest(FromNode, StandardMessages.GetStats);
        if (string.IsNullOrEmpty(XmlResult))
          return null;
        try
        {
          Converter.XmlToObject(XmlResult, typeof(Stats), out object ReturmObj);
          Stats = (Stats)ReturmObj;
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.Print(ex.Message);
          System.Diagnostics.Debugger.Break();
        }
      }
      return Stats;
    }
    private readonly int SpeedLimit = 1000;
    internal bool DecentralizedSpeedTest(Node NodeToTesting, out List<SpeedTestResult> SpeedTestResults)
    {
      SpeedTestResults = new List<SpeedTestResult>(); 
      int Best;
      var Connections = Network.MappingNetwork.GetConnections(0);
      if (Connections.Count == 0)
        Best = SpeedTest(NodeToTesting);
      else
      {
        var Speeds = new List<int>();
        foreach (var Node in Connections)
        {
          var XmlResult = SendRequest(Node, StandardMessages.RequestTestSpeed, NodeToTesting);
          if (!string.IsNullOrEmpty(XmlResult))
          {
            try
            {
              Converter.XmlToObject(XmlResult, typeof(SpeedTestResult), out object ReturmObj);
              SpeedTestResult Result = (SpeedTestResult)ReturmObj;
              SpeedTestResults.Add(Result);
              if (Result.VerifySignature(Node, NodeToTesting.IP))
                Speeds.Add(Result.Speed);
              else
              {
                Utility.Log("Node Error", "NodeIP=" + Node.IP + " Error signature in the result of the speed test of the new node");
                return false;
              }
            }
            catch (Exception ex)
            {
              System.Diagnostics.Debug.Print(ex.Message);
              System.Diagnostics.Debugger.Break();
              Utility.Log("Node Error", "NodeIP=" + Node.IP + " Error in DecentralizedSpeedTest");
              return false;
            }
          }
          else
            return false;
        }
        Speeds.Sort();
        Best = Speeds[0];
      }
      return Best != -1 && Best <= SpeedLimit;
    }
    internal SpeedTestResult SpeedTestSigned(Node NodeToTesting)
    {
      var Result = new SpeedTestResult() { Speed = SpeedTest(NodeToTesting), NodeIP = Network.MyNode.IP };
      Result.AddSignature(Network.MyNode, NodeToTesting.IP);
      return Result;
    }
    public class SpeedTestResult
    {

      public int Speed;
      public string Signature;
      /// <summary>
      /// IP of signature Node 
      /// </summary>
      public uint NodeIP;
      public long Timestamp;

      private byte[] Data(uint IPNodeToTesting)
      {
        return BitConverter.GetBytes(IPNodeToTesting).Concat(BitConverter.GetBytes(Speed)).Concat(BitConverter.GetBytes(Timestamp)).ToArray();
      }
      public void AddSignature(Node MyNode, uint IPNodeToTesting)
      {
        Signature = Converter.ByteArrayToString(MyNode.RSA.SignHash(Data(IPNodeToTesting), System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256")));
      }
      public bool VerifySignature(Node Node, uint NodeToTestingIP)
      {
        return NodeIP == Node.IP && Node.RSA.VerifyHash(Data(NodeToTestingIP), System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"), Converter.StringToByteArray(Signature));
      }
    }
    private int SpeedTest(Node NodeToTesting)
    {
      var Start = DateTime.UtcNow;
      for (int i = 0; i < 10; i++)
      {
        var XmlResult = SendRequest(NodeToTesting, StandardMessages.TestSpeed);
        if (XmlResult == null || XmlResult.Length != 131112)
          return -1;
      }
      var Speed = (int)(DateTime.UtcNow - Start).TotalMilliseconds;
      return Speed;
    }
    internal StandardAnsware AddToSharedBuffer(Node ToNode, Object Object)
    {
      if (Network.ThisNode.ConnectionStatus != StandardAnsware.Ok)
        return Network.ThisNode.ConnectionStatus;
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
      catch (Exception ex)
      {
        System.Diagnostics.Debug.Print(ex.Message);
        System.Diagnostics.Debugger.Break();
        return StandardAnsware.Error;
      }
    }
    internal class ResponseMonitor
    {
      public int ResponseCounter;
      public List<Node> Level0Connections;
    }
    /// <summary>
    /// It transfers a list of elements to a node, if this is the node at level 0, it means that these elements have just been taken into charge, it will then be distributed to all connections at level 0, collect all the signatures that certify the timestamp, and send the signatures to the nodes connected to level 0.
    /// This procedure is used to create a decentralized timestamp within the network.
    /// </summary>
    /// <param name="Elements">Element to send to the node</param>
    /// <param name="ToNode">Node that will receive the elements</param>
    /// <param name="ResponseMonitor">This parameter is specified only if we are at level 0 of the distribution of the elements, it is necessary to receive the timestamp signed by all the nodes connected to this level</param>
    internal void SendElementsToNode(List<BufferManager.ObjToNode> Elements, Node ToNode, ResponseMonitor ResponseMonitor = null)
    {
      new System.Threading.Thread(() =>
      {
        string XmlResult = null;
        //Verify if the node is disconnected
        if (Network.NodeList.Contains(ToNode))
        {
          XmlResult = SendRequest(ToNode, StandardMessages.SendElementsToNode, Elements);
          if (Utility.GetObjectName(XmlResult) == "TimestampVector")
          {
            if (Converter.XmlToObject(XmlResult, typeof(BufferManager.ObjToNode.TimestampVector), out object ObjTimestampVector))
            {
              var TimestampVector = (BufferManager.ObjToNode.TimestampVector)ObjTimestampVector;
              foreach (var Element in Elements)
              {
                if (TimestampVector.SignedTimestamp.TryGetValue(Element.ShortHash, out string SignedTimestamp))
                {
                  Element.SignedTimestamp += SignedTimestamp;
                }
              }
            }
          }
          if (ResponseMonitor != null)
          {
            ResponseMonitor.ResponseCounter += 1;
            if (ResponseMonitor.ResponseCounter == ResponseMonitor.Level0Connections.Count)
            {
              // All nodes connected to the zero level have signed the timestamp, now the signature of the timestamp of all the nodes must be sent to every single node.
              // This operation is used to create a decentralized timestamp.
              var TimestampVector = new BufferManager.ObjToNode.TimestampVector();
              foreach (var Element in Elements)
                TimestampVector.SignedTimestamp.Add(Element.ShortHash, Element.SignedTimestamp);
              foreach (var Node in ResponseMonitor.Level0Connections)
                SendTimestampSignatureToNode(TimestampVector, Node);
            }
          }
        }
      }).Start();
    }
    internal void SendTimestampSignatureToNode(BufferManager.ObjToNode.TimestampVector TimestampVector, Node ToNode)
    {
      new System.Threading.Thread(() =>
      {
        string XmlResult = null;
        //Verify if the node is disconnected
        if (Network.NodeList.Contains(ToNode))
        {
          XmlResult = SendRequest(ToNode, StandardMessages.SendTimestampSignatureToNode, TimestampVector);
        }
      }).Start();
    }
  }

}
