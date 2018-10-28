using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static NetworkManager.Network;

namespace NetworkManager
{
  /// <summary>
  /// The buffer is a mechanism that allows you to distribute objects on the network by assigning a timestamp.
  /// The objects inserted in the buffer will exit from it on all the nodes following the chronological order of input(they come out sorted by timestamp).
  /// The data output from the buffar must be managed by actions that are programmed when the network is initialized.
  /// </summary>
  internal class BufferManager
  {
    public BufferManager(Network Network)
    {
      this.Network = Network;
      Spooler = new Spooler(Network);
      BufferTimer = new System.Timers.Timer(1000) { AutoReset = true, Enabled = true };
      BufferTimer.Elapsed += (sender, e) => ToDoEverySec();

      NewStats = new System.Timers.Timer(43200000) { AutoReset = true, Enabled = true };//Every 12h
      NewStats.Elapsed += (sender, e) => NewStatsElapsed();

    }
    private Network Network;
    /// <summary>
    /// The spooler contains the logic that allows synchronization of data on the peer2peer network
    /// </summary>
    private Spooler Spooler;

    /// <summary>
    /// Send an object to the network to be inserted in the shared buffer
    /// </summary>
    /// <param name="Object">Object to send</param>
    /// <returns></returns>
    public Protocol.StandardAnsware AddToSaredBuffer(object Object)
    {
      return Network.Protocol.AddToSharedBuffer(Network.GetRandomNode(), Object);
    }
    private System.Timers.Timer BufferTimer;
    private void ToDoEverySec()
    {
      //var Buffer = BufferManager.Buffer;
      lock (Buffer)
      {
        var ToRemove = new List<ElementBuffer>();
        var ThisTime = Network.Now;
        foreach (var item in Buffer)
        {
          if ((ThisTime - new DateTime(item.Timestamp)) > Network.MappingNetwork.NetworkSyncTimeSpan)
          {
            ToRemove.Add(item);
            var ObjectName = Utility.GetObjectName(item.XmlObject);
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
        long Timestamp = Network.Now.Ticks;
        //var Element = new Element() { Timestamp = Timestamp, XmlObject = XmlObject };
        //At level 0 the timestamp will be assigned before transmission to the node in order to reduce the difference with the timestamp on the node
        var Element = new Element() { XmlObject = XmlObject };
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
    /// In this waiting list all the objects awaiting the timestamp signature are inserted by all the nodes assigned to the first level distribution
    /// </summary>
    private readonly List<ObjToNode> StandByList = new List<ObjToNode>();
    /// <summary>
    /// Insert the objects in the local buffer to be synchronized
    /// The elements come from other nodes
    /// </summary>
    /// <param name="Elements">Elements come from other nodes</param>
    internal ObjToNode.TimestampVector AddLocalFromNode(List<ObjToNode> Elements, Node FromNode)
    {
      ObjToNode.TimestampVector Result = null;
      lock (Buffer)
      {
        var Count = Buffer.Count;
        var ThisTime = Network.Now;
        foreach (var ObjToNode in Elements)
        {
          var TimePassedFromInsertion = ThisTime - new DateTime(ObjToNode.Timestamp);
          UpdateStats(TimePassedFromInsertion);
          if ((TimePassedFromInsertion) <= Network.MappingNetwork.NetworkSyncTimeSpan)
          {
            var Level = ObjToNode.Level + 1;
            var ElementBuffer = Buffer.Find((x) => x.Timestamp == ObjToNode.Timestamp && x.XmlObject == ObjToNode.XmlObject);
            if (ElementBuffer == null)
            {
              UpdateStats(TimePassedFromInsertion, true);
              if (ObjToNode.Level == 1)
              {
                // This is an object that is just inserted, so you must certify the timestamp and send the certificate to the node that took delivery of the object.
                // The object must then be put on standby until the node sends all the certificates for the timestamp.
                if (ObjToNode.CheckNodeThatStartedDistributingTheObject(FromNode))
                {
                  var Signature = ObjToNode.CreateTheSignatureForTheTimestamp(Network.MyNode, Network.Now);
                  StandByList.Add(ObjToNode);
                  if (Result == null)
                    Result = new ObjToNode.TimestampVector();
                  Result.SignedTimestamp.Add(ObjToNode.ShortHash, Signature);
                }
              }
              else
              {
                ElementBuffer = (ElementBuffer)(Element)ObjToNode;
                Buffer.Add(ElementBuffer);
              }
            }
            lock (ElementBuffer.Levels)
              if (!ElementBuffer.Levels.Contains(Level))
                ElementBuffer.Levels.Add(Level);
            lock (ElementBuffer.SendedNode)
              ElementBuffer.SendedNode.Add(FromNode);
            ElementBuffer.Received++;
          }
          else
          {
            //A dishonest node has started a fork through a fake timestamp?
            Stats24h.ElementsArrivedOutOfTime++;
            Stats12h.ElementsArrivedOutOfTime++;
          }
        }
        if (Count != Buffer.Count)
        {
          SortBuffer();
          Spooler.DataDelivery();
        }
      }
      return Result;
    }
    internal bool UnlockElementsInStandBy(ObjToNode.TimestampVector SignedTimestamps, Node FromNode)
    {
      lock (Buffer)
      {
        var Count = Buffer.Count();
        var Remove = new List<ObjToNode>();
        lock (StandByList)
        {
          foreach (var ObjToNode in StandByList)
            if (SignedTimestamps.SignedTimestamp.TryGetValue(ObjToNode.ShortHash, out string Signatures))
            {
              Remove.Add(ObjToNode);
              ObjToNode.SignedTimestamp = Signatures;
              if (ObjToNode.CheckSignedTimestamp(Network) == ObjToNode.CheckSignedTimestampResult.Ok)
              {
                Buffer.Add((ElementBuffer)(Element)ObjToNode);
              }
            }
          foreach (var item in Remove)
            StandByList.Remove(item);
        }
        if (Count != Buffer.Count())
        {
          SortBuffer();
          Spooler.DataDelivery();
        }
      }
      return true;
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
      public long Timestamp;
      public string XmlObject;
    }
    public class ObjToNode : Element
    {
      public int Level;
      public string SignedTimestamp;
      /// <summary>
      /// Class used exclusively to transmit the timestamp certificates to the node who sent the object to be signed
      /// </summary>
      public class TimestampVector
      {
        // "int" is the short hash of ObjToNode and "string" is the SignedTimestamp for the single ObjToNode
        public Dictionary<int, string> SignedTimestamp = new Dictionary<int, string>();
      }
      internal int ShortHash;
      private byte[] Hash()
      {
        var Data = BitConverter.GetBytes(Timestamp).Concat(Converter.StringToByteArray(XmlObject)).ToArray();
        System.Security.Cryptography.HashAlgorithm hashType = new System.Security.Cryptography.SHA256Managed();
        byte[] hashBytes = hashType.ComputeHash(Data);
        ShortHash = hashBytes.GetHashCode();
        return hashBytes;
      }
      /// <summary>
      /// Check the node that sent this object, have assigned a correct timestamp, if so it generates its own signature.
      /// </summary>
      /// <param name="MyNode">Your own Node</param>
      /// <returns>Returns the signature if the timestamp assigned by the node is correct, otherwise null</returns>
      internal string CreateTheSignatureForTheTimestamp(Node MyNode, DateTime Now )
      {
        var ThisMoment = Now;
        var DT = new DateTime(Timestamp);
        var Margin = 0.5; // Calculates a margin because the clocks on the nodes may not be perfectly synchronized
        if (ThisMoment >= DT.AddSeconds(-Margin))
        {
          var MaximumTimeToTransmitTheDataOnTheNode = 2; // In seconds
          if (ThisMoment <= DT.AddSeconds(MaximumTimeToTransmitTheDataOnTheNode + Margin))
          {
            // Ok, I can certify the date and time
            return GetTimestamp(MyNode);
          }
        }
        return null;
      }
      private string GetTimestamp(Node Node)
      {
        var IpNode = BitConverter.GetBytes(Node.IP);
        var SignedTimestamp = Node.RSA.SignHash(Hash(), System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"));
        return Convert.ToBase64String(IpNode.Concat(SignedTimestamp).ToArray());
      }
      internal void AddFirstTimestamp(Node NodeLevel0, long Timestamp)
      {
        this.Timestamp = Timestamp;
        this.SignedTimestamp = GetTimestamp(NodeLevel0);
      }
      internal enum CheckSignedTimestampResult { Ok, NodeThatPutTheSignatureNotFound, InvalidSignature, NonCompliantNetworkConfiguration }
      /// <summary>
      /// Check if all the nodes responsible for distributing this data have put their signature on the timestamp
      /// </summary>
      /// <param name="Network"></param>
      /// <param name="CurrentNodeList"></param>
      /// <param name="NodesRemoved"></param>
      /// <returns>Result of the operation</returns>
      internal CheckSignedTimestampResult CheckSignedTimestamp(Network Network)
      {
        var CurrentNodeList = Network.NodeList;
        var NodesRemoved = new List<Node>(); //=============== sistemare questo valore e finire ===================================
        var LenT = 30;
        var SignedTimestamps = Convert.FromBase64String(SignedTimestamp);
        var TS = new List<Byte[]>();
        Node FirstNode = null;
        var Nodes = new List<Node>();
        do
        {
          var Timestamp = SignedTimestamps.Take(LenT).ToArray();
          TS.Add(Timestamp);
          SignedTimestamps = SignedTimestamps.Skip(LenT).ToArray();
        } while (SignedTimestamps.Count() != 0);
        byte[] HashBytes = Hash();
        foreach (var SignedTS in TS)
        {
          ReadSignedTimespan(SignedTS, out uint IpNode, out byte[] Signature);
          var Node = CurrentNodeList.Find((x) => x.IP == IpNode);
          if (Node == null)
            Node = NodesRemoved.Find((x) => x.IP == IpNode);
          if (FirstNode == null)
            FirstNode = Node;
          if (Node == null)
          {
            return CheckSignedTimestampResult.NodeThatPutTheSignatureNotFound;
          }
          else
          {
            Nodes.Add(Node);
            if (!Node.RSA.VerifyHash(HashBytes, System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"), Signature))
            {
              return CheckSignedTimestampResult.InvalidSignature;
            }
          }
        }
        var Connections = Network.MappingNetwork.GetConnections(1, FirstNode);
        if (Connections.Count != Nodes.Count)
          return CheckSignedTimestampResult.NonCompliantNetworkConfiguration;
        else
        {
          foreach (var item in Connections)
          {
            if (!Nodes.Contains(item))
            {
              return CheckSignedTimestampResult.NonCompliantNetworkConfiguration;
            }
          }
        }
        return CheckSignedTimestampResult.Ok;
      }
      internal bool CheckNodeThatStartedDistributingTheObject(Node FromNode)
      {
        ReadSignedTimespan(Convert.FromBase64String(SignedTimestamp), out uint IpNode, out byte[] Signature);
        if (FromNode.IP != IpNode)
          return false;
        if (!FromNode.RSA.VerifyHash(Hash(), System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"), Signature))
          return false;
        return true;
      }
      private static void ReadSignedTimespan(byte[] SignedTimestamp, out uint IpNode, out byte[] Signature)
      {
        IpNode = BitConverter.ToUInt32(SignedTimestamp, 0);
        Signature = SignedTimestamp.Skip(4).ToArray();
      }
    }
    /// <summary>
    /// Add a action used to local sync the objects coming from the buffer
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
  }

}
