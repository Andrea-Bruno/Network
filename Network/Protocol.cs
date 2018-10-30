using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace NetworkManager
{
  public class Protocol
  {
    public delegate object GetObject(string xmlObject);

    public enum StandardAnsware
    {
      Ok,
      Disconnected,
      Error,
      DuplicateIp,
      TooSlow,
      Failure,
      NoAnsware,
      Declined,
      IpError,
      Unauthorized
    }

    private readonly int _speedLimit = 1000;
    private readonly Network _network;
    internal Dictionary<string, GetObject> OnReceivingObjectsActions = new Dictionary<string, GetObject>();
    internal Dictionary<string, GetObject> OnRequestActions = new Dictionary<string, GetObject>();

    internal Protocol(Network network)
    {
      _network = network;
    }

    public bool AddOnReceivingObjectAction(string nameTypeObject, GetObject getObject)
    {
      lock (OnReceivingObjectsActions)
      {
        if (OnReceivingObjectsActions.ContainsKey(nameTypeObject))
          return false;
        OnReceivingObjectsActions.Add(nameTypeObject, getObject);
      }

      return true;
    }

    public bool AddOnRequestAction(string actionName, GetObject getObject)
    {
      lock (OnRequestActions)
      {
        if (OnRequestActions.ContainsKey(actionName))
          return false;
        OnRequestActions.Add(actionName, getObject);
      }

      return true;
    }

    private string NotifyToNode(Node toNode, string request, object obj = null)
    {
      var Try = 0;
      string xmlResult;
      do
      {
        Try += 1;
        //if (ToNode == null)
        //  ToNode = GetRandomNode();
        if (toNode == null)
          return "";
        xmlResult = _network.Comunication.GetObjectSync(toNode.Address, request, obj, toNode.MachineName + ".");
      } while (string.IsNullOrEmpty(xmlResult) && Try <= 10);
      if (Try <= 10) return xmlResult;
      Debugger.Break();
      _network.IsOnline = false;
      _network.OnlineDetection.WaitForInternetConnection();

      return xmlResult;
    }

    private string SendRequest(Node toNode, StandardMessages message, object obj = null)
    {
      return NotifyToNode(toNode, message.ToString(), obj);
    }

    internal List<Node> GetNetworkNodes(Node entryPoint)
    {
      var xmlResult = SendRequest(entryPoint, StandardMessages.NetworkNodes);
      if (string.IsNullOrEmpty(xmlResult))
        return new List<Node>();
      Converter.XmlToObject(xmlResult, typeof(List<Node>), out var returmObj);
      var nodeList = (List<Node>) returmObj;
      return nodeList;
    }

    internal StandardAnsware ImOffline(Node toNode, Node myNode)
    {
      var xmlResult = SendRequest(toNode, StandardMessages.ImOffline, myNode);
      if (string.IsNullOrEmpty(xmlResult)) return StandardAnsware.Error;
      try
      {
        Converter.XmlToObject(xmlResult, typeof(StandardAnsware), out var answare);
        return (StandardAnsware) answare;
      }
      catch (Exception ex)
      {
        Debug.Print(ex.Message);
        Debugger.Break();
      }

      return StandardAnsware.Error;
    }

    internal StandardAnsware ImOnline(Node toNode, Node myNode)
    {
      var xmlResult = SendRequest(toNode, StandardMessages.ImOnline, myNode);
      if (string.IsNullOrEmpty(xmlResult))
        return StandardAnsware.NoAnsware;
      try
      {
        Converter.XmlToObject(xmlResult, typeof(StandardAnsware), out var answare);
        return (StandardAnsware) answare;
      }
      catch (Exception ex)
      {
        Debug.Print(ex.Message);
        Debugger.Break();
      }

      return StandardAnsware.Error;
    }

    internal Stats GetStats(Node fromNode)
    {
      if (fromNode == null) return null;
      Stats stats = null;
      var xmlResult = SendRequest(fromNode, StandardMessages.GetStats);
      if (string.IsNullOrEmpty(xmlResult))
        return null;
      try
      {
        Converter.XmlToObject(xmlResult, typeof(Stats), out var returmObj);
        stats = (Stats) returmObj;
      }
      catch (Exception ex)
      {
        Debug.Print(ex.Message);
        Debugger.Break();
      }

      return stats;
    }

    internal bool DecentralizedSpeedTest(Node nodeToTesting, out List<SpeedTestResult> speedTestResults)
    {
      speedTestResults = new List<SpeedTestResult>();
      var connections = _network.MappingNetwork.GetConnections(0);
      var speedSigned = SpeedTestSigned(nodeToTesting);
      speedTestResults.Add(speedSigned);
      var speeds = new List<int> {speedSigned.Speed};
      if (connections.Count != 0)
        foreach (var node in connections)
        {
          var xmlResult = SendRequest(node, StandardMessages.RequestTestSpeed, nodeToTesting);
          if (!string.IsNullOrEmpty(xmlResult))
            try
            {
              Converter.XmlToObject(xmlResult, typeof(SpeedTestResult), out var returmObj);
              var result = (SpeedTestResult) returmObj;
              speedTestResults.Add(result);
              if (result.VerifySignature(node, nodeToTesting.Ip))
              {
                speeds.Add(result.Speed);
              }
              else
              {
                Utility.Log("Node Error",
                  "NodeIP=" + node.Ip + " Error signature in the result of the speed test of the new node");
                return false;
              }
            }
            catch (Exception ex)
            {
              Debug.Print(ex.Message);
              Debugger.Break();
              Utility.Log("Node Error", "NodeIP=" + node.Ip + " Error in DecentralizedSpeedTest");
              return false;
            }
          else
            return false;
        }

      speeds.Sort();
      var best = speeds[0];
      return best != -1 && best <= _speedLimit;
    }

    internal SpeedTestResult SpeedTestSigned(Node nodeToTesting)
    {
      var result = new SpeedTestResult {Speed = SpeedTest(nodeToTesting), NodeIp = _network.MyNode.Ip};
      result.SignTheResult(_network.MyNode, nodeToTesting.Ip, _network.Now.Ticks);
      return result;
    }

    private int SpeedTest(Node nodeToTesting)
    {
      var start = DateTime.UtcNow;
      for (var i = 0; i < 10; i++)
      {
        var xmlResult = SendRequest(nodeToTesting, StandardMessages.TestSpeed);
        if (xmlResult == null || xmlResult.Length != 131112)
          return -1;
      }

      var speed = (int) (DateTime.UtcNow - start).TotalMilliseconds;
      return speed;
    }

    internal bool NotificationNewNodeIsOnline(Node node, List<SpeedTestResult> speedTestResults)
    {
      var notification = new NodeOnlineNotification {Node = node, Signatures = speedTestResults};
      return _network.BufferManager.AddLocal(notification);
    }

    internal StandardAnsware AddToSharedBuffer(Node toNode, object Object)
    {
      if (_network.ThisNode.ConnectionStatus != StandardAnsware.Ok)
        return _network.ThisNode.ConnectionStatus;
      try
      {
        var xmlResult = SendRequest(toNode, StandardMessages.AddToBuffer, Object);
        //var XmlResult = Comunication.SendObjectSync(Object, Node.Address, null, Node.MachineName);
        if (string.IsNullOrEmpty(xmlResult)) return StandardAnsware.NoAnsware;

        Converter.XmlToObject(xmlResult, typeof(StandardAnsware), out var returmObj);
        var answare = (StandardAnsware) returmObj;
        return answare;
      }
      catch (Exception ex)
      {
        Debug.Print(ex.Message);
        Debugger.Break();
        return StandardAnsware.Error;
      }
    }

    /// <summary>
    ///   It transfers a list of elements to a node, if this is the node at level 0, it means that these elements have just
    ///   been taken into charge, it will then be distributed to all connections at level 0, collect all the signatures that
    ///   certify the timestamp, and send the signatures to the nodes connected to level 0.
    ///   This procedure is used to create a decentralized timestamp within the network.
    /// </summary>
    /// <param name="elements">Element to send to the node</param>
    /// <param name="toNode">Node that will receive the elements</param>
    /// <param name="responseMonitor">
    ///   This parameter is specified only if we are at level 0 of the distribution of the
    ///   elements, it is necessary to receive the timestamp signed by all the nodes connected to this level
    /// </param>
    internal void SendElementsToNode(List<ObjToNode> elements, Node toNode, ResponseMonitor responseMonitor = null)
    {
      new Thread(() =>
      {
        string xmlResult = null;
        //Verify if the node is disconnected
        if (!_network.NodeList.Contains(toNode)) return;
        xmlResult = SendRequest(toNode, StandardMessages.SendElementsToNode, elements);
        if (Utility.GetObjectName(xmlResult) == "TimestampVector")
          if (Converter.XmlToObject(xmlResult, typeof(ObjToNode.TimestampVector), out var objTimestampVector))
          {
            var timestampVector = (ObjToNode.TimestampVector) objTimestampVector;
            foreach (var element in elements)
              if (timestampVector.SignedTimestamp.TryGetValue(element.ShortHash, out var signedTimestamp))
                element.TimestampSignature += signedTimestamp;
          }

        if (responseMonitor == null) return;
        {
          responseMonitor.ResponseCounter += 1;
          if (responseMonitor.ResponseCounter == responseMonitor.Level0Connections.Count)
          {
            // All nodes connected to the zero level have signed the timestamp, now the signature of the timestamp of all the nodes must be sent to every single node.
            // This operation is used to create a decentralized timestamp.
            var timestampVector = new ObjToNode.TimestampVector();
            foreach (var element in elements)
              timestampVector.SignedTimestamp.Add(element.ShortHash, element.TimestampSignature);
            foreach (var node in responseMonitor.Level0Connections)
              SendTimestampSignatureToNode(timestampVector, node);
          }
        }
      }).Start();
    }

    internal void SendTimestampSignatureToNode(ObjToNode.TimestampVector timestampVector, Node toNode)
    {
      new Thread(() =>
      {
        string xmlResult = null;
        //Verify if the node is disconnected
        if (_network.NodeList.Contains(toNode))
          xmlResult = SendRequest(toNode, StandardMessages.SendTimestampSignatureToNode, timestampVector);
      }).Start();
    }

    internal enum StandardMessages
    {
      NetworkNodes,
      ImOnline,
      ImOffline,
      RequestTestSpeed,
      TestSpeed,
      NotificationNewNodeIsOnline,
      AddToBuffer,
      SendElementsToNode,
      SendTimestampSignatureToNode,
      GetStats
    }

    public class Stats
    {
      /// <summary>
      ///   Maximum time to update the node
      /// </summary>
      public int NetworkLatency;
    }

    public class SpeedTestResult
    {
      /// <summary>
      ///   IP of signature Node
      /// </summary>
      public uint NodeIp;

      public string Signature;

      public int Speed;
      public long Timestamp;

      private byte[] HashData(uint ipNodeToTesting)
      {
        var data = BitConverter.GetBytes(ipNodeToTesting).Concat(BitConverter.GetBytes(Speed))
          .Concat(BitConverter.GetBytes(Timestamp)).ToArray();
        return Utility.GetHash(data);
      }

      public void SignTheResult(Node myNode, uint ipNodeToTesting, long currentTimestamp)
      {
        Timestamp = currentTimestamp;
        Signature = Convert.ToBase64String(myNode.Rsa.SignHash(HashData(ipNodeToTesting),
          CryptoConfig.MapNameToOID("SHA256")));
      }

      public bool VerifySignature(Node nodeOfSignature, uint ipNewNode)
      {
        return NodeIp == nodeOfSignature.Ip && nodeOfSignature.Rsa.VerifyHash(HashData(ipNewNode),
                 CryptoConfig.MapNameToOID("SHA256"), Convert.FromBase64String(Signature));
      }
    }

    public class NodeOnlineNotification
    {
      public Node Node;
      public List<SpeedTestResult> Signatures;
    }

    internal class ResponseMonitor
    {
      public List<Node> Level0Connections;
      public int ResponseCounter;
    }
  }
}