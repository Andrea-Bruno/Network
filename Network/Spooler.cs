using System;
using System.Collections.Generic;
using System.Text;
using static NetworkManager.BufferManager;
using static NetworkManager.Network;

namespace NetworkManager
{
  /// <summary>
  /// The spooler contains the logic that allows synchronization of data on the peer2peer network
  /// </summary>
  internal class Spooler
  {
    public Spooler(Network network)
    {
      _network = network;
      _bufferManager = network.BufferManager;
      _mappingNetwork = network.MappingNetwork;
      _spoolerTimer = new System.Timers.Timer(_pauseBetweenTransmissionOnTheNode) { AutoReset = true, Enabled = true };
      _spoolerTimer.Elapsed += (sender, e) => DataDelivery();
    }
    private readonly Network _network;
    private readonly BufferManager _bufferManager;
    private readonly MappingNetwork _mappingNetwork;
    private readonly int _pauseBetweenTransmissionOnTheNode = 2000;
    /// <summary>
    /// Memorize when the last communication was made at a certain level
    /// </summary>
    private readonly Dictionary<int, DateTime> _lastTransmission = new Dictionary<int, DateTime>();
    private readonly System.Timers.Timer _spoolerTimer;
    internal void DataDelivery()
    {
      List<Node> level0Connections = null;//The transmissions at this level will receive the signature of the timestamp from the node that receives them, these signatures once received all must be sent to every single node of this level
      var dataToNode = new Dictionary<Node, List<ObjToNode>>();
      var levels = new List<int>();
      levels.Sort();
      lock (_network.BufferManager.Buffer)
        foreach (var data in _network.BufferManager.Buffer)
        {
          levels.AddRange(data.Levels.FindAll(x => !levels.Contains(x)));
        }
      foreach (var level in levels)
      {
        var msFromLastTransmissionAtThisLevel = int.MaxValue;
        lock (_lastTransmission)
          if (_lastTransmission.TryGetValue(level, out var trasmissionTime))
            msFromLastTransmissionAtThisLevel = (int)(DateTime.UtcNow - trasmissionTime).TotalMilliseconds;
        if (msFromLastTransmissionAtThisLevel <= _pauseBetweenTransmissionOnTheNode) continue;
        var connections = _network.MappingNetwork.GetConnections(level);
        if (level == 0)
          level0Connections = connections;
        lock (_network.BufferManager.Buffer)
          foreach (var elementBuffer in _network.BufferManager.Buffer)
            if (elementBuffer.Levels.Contains(level))
            {
              foreach (var node in connections)
                if (!elementBuffer.SendedNode.Contains(node))
                {
                  if (!dataToNode.TryGetValue(node, out var toSendToNode))
                  {
                    toSendToNode = new List<ObjToNode>();
                    dataToNode.Add(node, toSendToNode);
                  }
                  var elementToNode = new ObjToNode(elementBuffer.Element) { Level = level };
                  if (level == 0)
                  {
                    // We assign the timestamp and sign it
                    // The nodes of level 1 that will receive this element, will verify the timestamp and if congruous they sign it and return the signature in response to the forwarding.
                    elementToNode.AddFirstTimestamp(_network.MyNode, _network.Now.Ticks);
                  }
                  toSendToNode.Add(elementToNode);
                  lock (elementBuffer.SendedNode)
                    elementBuffer.SendedNode.Add(node);
                  lock (_lastTransmission)
                  {
                    _lastTransmission.Remove(level);
                    _lastTransmission.Add(level, DateTime.UtcNow);
                  }
                }
            }
      }
      var responseMonitorForLevel0 = new Protocol.ResponseMonitor
      {
        Level0Connections = level0Connections
      };
      foreach (var toSend in dataToNode)
      {
        if (level0Connections != null && level0Connections.Contains(toSend.Key))
          _network.Protocol.SendElementsToNode(toSend.Value, toSend.Key, responseMonitorForLevel0);
        else
          _network.Protocol.SendElementsToNode(toSend.Value, toSend.Key);
      }
    }
  }

}
