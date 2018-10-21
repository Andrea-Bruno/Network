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
    public Spooler(Network Network)
    {
      this.Network = Network;
      this.BufferManager = Network.BufferManager;
      this.MappingNetwork = Network.MappingNetwork;
      SpoolerTimer = new System.Timers.Timer(PauseBetweenTransmissionOnTheNode) { AutoReset = true, Enabled = true };
      SpoolerTimer.Elapsed += (sender, e) => DataDelivery();
    }
    private Network Network;
    private readonly BufferManager BufferManager;
    private readonly MappingNetwork MappingNetwork;
    private readonly int PauseBetweenTransmissionOnTheNode = 2000;
    /// <summary>
    /// Memorize when the last communication was made at a certain level
    /// </summary>
    private Dictionary<int, DateTime> LastTransmission = new Dictionary<int, DateTime>();
    private System.Timers.Timer SpoolerTimer;
    internal void DataDelivery()
    {
      List<Node> Level0Connections = null;//The transmissions at this level will receive the signature of the timestamp from the node that receives them, these signatures once received all must be sent to every single node of this level
      var DataToNode = new Dictionary<Node, List<ObjToNode>>();
      var Levels = new List<int>();
      Levels.Sort();
      lock (Network.BufferManager.Buffer)
        foreach (var Data in Network.BufferManager.Buffer)
        {
          Levels.AddRange(Data.Levels.FindAll(x => !Levels.Contains(x)));
        }
      foreach (var Level in Levels)
      {
        var MsFromLastTransmissionAtThisLevel = int.MaxValue;
        lock (LastTransmission)
          if (LastTransmission.TryGetValue(Level, out DateTime TrasmissionTime))
            MsFromLastTransmissionAtThisLevel = (int)(DateTime.UtcNow - TrasmissionTime).TotalMilliseconds;
        if (MsFromLastTransmissionAtThisLevel > PauseBetweenTransmissionOnTheNode)
        {
          var Connections = Network.MappingNetwork.GetConnections(Level);
          if (Level == 0)
            Level0Connections = Connections;
          lock (Network.BufferManager.Buffer)
            foreach (var Data in Network.BufferManager.Buffer)
              if (Data.Levels.Contains(Level))
              {
                foreach (var Node in Connections)
                  if (!Data.SendedNode.Contains(Node))
                  {
                    if (!DataToNode.TryGetValue(Node, out List<ObjToNode> ToSendToNode))
                    {
                      ToSendToNode = new List<ObjToNode>();
                      DataToNode.Add(Node, ToSendToNode);
                    }
                    var Element = (Element)Data;
                    var ElementToNode = (ObjToNode)Element;
                    ElementToNode.Level = Level;
                    if (Level == 0)
                    {
                      // We assign the timestamp and sign it
                      // The nodes of level 1 that will receive this element, will verify the timestamp and if congruous they sign it and return the signature in response to the forwarding.
                      ElementToNode.AddFirstTimestamp(Network.MyNode, Network.Now.Ticks);
                    }
                    ToSendToNode.Add(ElementToNode);
                    lock (Data.SendedNode)
                      Data.SendedNode.Add(Node);
                    lock (LastTransmission)
                    {
                      LastTransmission.Remove(Level);
                      LastTransmission.Add(Level, DateTime.UtcNow);
                    }
                  }
              }
        }
      }
      var ResponseMonitorForLevel0 = new Protocol.ResponseMonitor
      {
        Level0Connections = Level0Connections
      };
      foreach (var ToSend in DataToNode)
      {
        if (Level0Connections != null && Level0Connections.Contains(ToSend.Key))
          Network.Protocol.SendElementsToNode(ToSend.Value, ToSend.Key, ResponseMonitorForLevel0);
        else
          Network.Protocol.SendElementsToNode(ToSend.Value, ToSend.Key);
      }
    }
  }

}
