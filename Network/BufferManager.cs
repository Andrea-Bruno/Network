using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using static NetworkManager.Network;

namespace NetworkManager
{
  /// <summary>
  /// The buffer is a mechanism that allows you to distribute objects on the network by assigning a timestamp.
  /// The objects inserted in the buffer will exit from it on all the nodes following the chronological order of input(they come out sorted by timestamp).
  /// The data output from the buffer must be managed by actions that are programmed when the network is initialized.
  /// </summary>
  internal class BufferManager
  {
    public BufferManager(Network network)
    {
      _network = network;
      _spooler = new Spooler(network);
      var bufferTimer = new Timer(1000) { AutoReset = true, Enabled = true };
      bufferTimer.Elapsed += (sender, e) => Scheduler();
      var newStats = new Timer(43200000) { AutoReset = true, Enabled = true };
      newStats.Elapsed += (sender, e) => NewStatsElapsed();
    }

    private readonly Network _network;
    /// <summary>
    /// The spooler contains the logic that allows synchronization of data on the peer2peer network
    /// </summary>
    private readonly Spooler _spooler;

    /// <summary>
    /// Send an object to the network to be inserted in the shared buffer
    /// </summary>
    /// <param name="Object">Object to send</param>
    /// <returns></returns>
    public Protocol.StandardAnswer AddToSharedBuffer(object Object)
    {
      return _network.Protocol.AddToSharedBuffer(_network.GetRandomNode(), Object);
    }

    private void Scheduler()
    {
      //var Buffer = BufferManager.Buffer;
      lock (Buffer)
      {
        var toRemove = new List<ElementBuffer>();
        var thisTime = _network.Now;
        foreach (var item in Buffer)
        {
          //For objects of level 0 the timestamp is added by the spooler at the time of forwarding in order to reduce the time difference between the timestamp and the reception of the object on the other node
          if (item.Element.Timestamp == 0) continue;
          if ((thisTime - new DateTime(item.Element.Timestamp)) > _network.MappingNetwork.NetworkSyncTimeSpan)
          {
            toRemove.Add(item);
            var objectName = Utility.GetObjectName(item.Element.XmlObject);
            OnReceiveObjectFromBuffer(objectName, item.Element.XmlObject, item.Element.Timestamp);
          }
          else
            break;//because the buffer is sorted by Timespan
        }
        foreach (var item in toRemove)
          Buffer.Remove(item);
      }
    }
    private void OnReceiveObjectFromBuffer(string objectName, string xmlObject, long timestamp)
    {
      if (objectName == "NodeOnlineNotification")
        if (Converter.XmlToObject(xmlObject, typeof(Protocol.NodeOnlineNotification), out var obj))
        {
          var nodeOnlineNotification = (Protocol.NodeOnlineNotification)(obj);
          if (nodeOnlineNotification.Node.CheckIp())
          {
            var invalid = false;
            var connections = new List<Node>();
            Node nodeAtLevel0 = null;
            foreach (var item in nodeOnlineNotification.Signatures)
            {
              var nodeOfSignature = _network.NodeList.CurrentAndRecentNodes().Find(x => x.Ip == item.NodeIp);
              if (nodeOfSignature == null)
              {
                invalid = true;
                break;
              }
              if (nodeAtLevel0 == null)
                nodeAtLevel0 = nodeOfSignature;
              else
                connections.Add(nodeOfSignature);
              if (item.VerifySignature(nodeOfSignature, nodeOnlineNotification.Node.Ip)) continue;
              invalid = true;
              break;
            }
            if (!invalid)
              if (_network.ValidateConnectionAtLevel0(nodeAtLevel0, connections))
                _network.NodeList.Add(nodeOnlineNotification.Node, timestamp);
          }
        }

      if (_bufferCompletedAction.TryGetValue(objectName, out var action))
        action.Invoke(xmlObject, timestamp);
      //foreach (SyncData Action in BufferCompletedAction)
      //  Action.Invoke(item.XmlObject, item.Timestamp);
    }
    /// <summary>
    /// Insert the object in the local buffer to be synchronized
    /// Object is a new element inserted by an external user
    /// </summary>
    /// <param name="Object">Object</param>
    /// <returns></returns>
    internal bool AddLocal(object Object)
    {
      var xmlObject = Converter.ObjectToXml(Object);
      return AddLocal(xmlObject);
    }
    /// <summary>
    /// Insert the object in the local buffer to be synchronized
    /// xmlObject is a new element inserted by an external user
    /// </summary>
    /// <param name="xmlObject">Serialized object im format xml</param>
    /// <returns></returns>
    internal bool AddLocal(string xmlObject)
    {
      if (string.IsNullOrEmpty(xmlObject))
        return false;
      //long Timestamp = Network.Now.Ticks;
      //var Element = new Element() { Timestamp = Timestamp, XmlObject = XmlObject };
      //At level 0 the timestamp will be assigned before transmission to the node in order to reduce the difference with the timestamp on the node
      var element = new Element() { XmlObject = xmlObject };
      lock (Buffer)
      {
        var elementBuffer = new ElementBuffer(element);
        elementBuffer.Levels.Add(1);
        Buffer.Add(elementBuffer);
        SortBuffer();
      }
      _spooler.DataDelivery();
      return true;
    }

    /// <summary>
    /// In this waiting list all the objects awaiting the timestamp signature are inserted by all the nodes assigned to the first level distribution
    /// </summary>
    private readonly List<ObjToNode> _standByList = new List<ObjToNode>();

    /// <summary>
    /// Insert the objects in the local buffer to be synchronized
    /// The elements come from other nodes
    /// </summary>
    /// <param name="elements">Elements come from other nodes</param>
    /// <param name="fromNode">From which node comes the element</param>
    internal ObjToNode.TimestampVector AddLocalFromNode(IEnumerable<ObjToNode> elements, Node fromNode)
    {
      ObjToNode.TimestampVector result = null;
      lock (Buffer)
      {
        var count = Buffer.Count;
        var thisTime = _network.Now;
        foreach (var objToNode in elements)
        {
          var timePassedFromInsertion = thisTime - new DateTime(objToNode.Timestamp);
          UpdateStats(timePassedFromInsertion);
          if ((timePassedFromInsertion) <= _network.MappingNetwork.NetworkSyncTimeSpan)
          {
            if (objToNode.Level == 1)
            {
              UpdateStats(timePassedFromInsertion, true);
              // This is an object that is just inserted, so you must certify the timestamp and send the certificate to the node that took delivery of the object.
              // The object must then be put on standby until the node sends all the certificates for the timestamp.
              if (objToNode.CheckNodeThatStartedDistributingTheObject(fromNode))
              {
                var signature = objToNode.CreateTheSignatureForTheTimestamp(_network.MyNode, _network.Now);
                _standByList.Add(objToNode);
                if (result == null) result = new ObjToNode.TimestampVector();
                result.SignedTimestamp.Add(objToNode.ShortHash, signature);
              }
              else
              {
                Utility.Log("security", "Check failure fromNode " + fromNode.Ip);
                System.Diagnostics.Debugger.Break();
              }
            }
            else
            {
              var level = objToNode.Level + 1;
              var elementBuffer = Buffer.Find(x => x.Element.Timestamp == objToNode.Timestamp && x.Element.XmlObject == objToNode.XmlObject);
              if (elementBuffer == null)
              {
                UpdateStats(timePassedFromInsertion, true);
                elementBuffer = new ElementBuffer(objToNode.GetElement);
                Buffer.Add(elementBuffer);
              }
              lock (elementBuffer.Levels)
                if (elementBuffer.Levels.Contains(level))
                  elementBuffer.Levels.Add(level);
              lock (elementBuffer.SendedNode)
                elementBuffer.SendedNode.Add(fromNode);
              elementBuffer.Received++;
            }
          }
          else
          {
            //A dishonest node has started a fork through a fake timestamp?
            Stats24H.ElementsArrivedOutOfTime++;
            _stats12H.ElementsArrivedOutOfTime++;
          }
        }
        if (count == Buffer.Count) return result;
        SortBuffer();
        _spooler.DataDelivery();
      }
      return result;
    }
    internal bool UnlockElementsInStandBy(ObjToNode.TimestampVector signedTimestamps, Node fromNode)
    {
      lock (Buffer)
      {
        var count = Buffer.Count();
        var remove = new List<ObjToNode>();
        lock (_standByList)
        {
          foreach (var objToNode in _standByList)
            if (signedTimestamps.SignedTimestamp.TryGetValue(objToNode.ShortHash, out var signatures))
            {
              remove.Add(objToNode);
              objToNode.TimestampSignature = signatures;
              if (objToNode.CheckSignedTimestamp(_network) == ObjToNode.CheckSignedTimestampResult.Ok)
              {
                Buffer.Add(new ElementBuffer(objToNode.GetElement));
              }
            }
          foreach (var item in remove)
            _standByList.Remove(item);
        }
        if (count == Buffer.Count()) return true;
        SortBuffer();
        _spooler.DataDelivery();
      }
      return true;
    }
    private void UpdateStats(TimeSpan value, bool firstAdd = false)
    {
      Stats24H.AddValue(value, firstAdd);
      _stats12H.AddValue(value, firstAdd);
    }
    private void NewStatsElapsed()
    {
      Stats24H = _stats12H;
      _stats12H = new Statistics();
    }
    internal Statistics Stats24H = new Statistics();
    private Statistics _stats12H = new Statistics();
    internal class Statistics
    {
      internal void AddValue(TimeSpan value, bool firstAdd)
      {
        ReceivedElements++;
        if (firstAdd)
        {
          MaximumArrivalTimeElement = value;
          if (value > MaximumArrivalTimeElement)
            ReceivedUnivocalElements++;
        }
        if (value > NetworkLatency)
          NetworkLatency = value;
      }
      internal int ReceivedElements = 0;
      internal int ReceivedUnivocalElements = 0;
      internal int ElementsArrivedOutOfTime = 0;
      internal TimeSpan MaximumArrivalTimeElement; /// Maximum time to update the node (from the first node)
      internal TimeSpan NetworkLatency; /// Maximum time to update the node (from all node)
    }

    private void SortBuffer()
    {
      Buffer = Buffer.OrderBy(x => x.Element.XmlObject).ToList();//Used for the element whit same Timestamp
      Buffer = Buffer.OrderBy(x => x.Element.Timestamp).ToList();
    }
    internal List<ElementBuffer> Buffer = new List<ElementBuffer>();
    internal class ElementBuffer
    {
      public ElementBuffer(Element element)
      {
        Element = element;
      }
      public Element Element;
      public List<Node> SendedNode = new List<Node>();
      public int Received;
      public List<int> Levels = new List<int>();
    }
    /// <summary>
    /// Add a action used to local sync the objects coming from the buffer
    /// </summary>
    /// <param name="action">Action to execute for every object</param>
    /// <param name="forObjectName">Indicates what kind of objects will be treated by this action</param>
    public bool AddSyncDataAction(SyncData action, string forObjectName)
    {
      if (_bufferCompletedAction.ContainsKey(forObjectName))
        return false;
      _bufferCompletedAction.Add(forObjectName, action);
      return true;
    }
    private readonly Dictionary<string, SyncData> _bufferCompletedAction = new Dictionary<string, SyncData>();
  }

}
