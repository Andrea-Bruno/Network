using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;

namespace NetworkManager
{
	/// <summary>
	/// The spooler contains the logic that allows synchronization of data on the peer2peer networkConnection
	/// </summary>
	internal class Spooler
	{
		public Spooler(NetworkConnection networkConnection)
		{
			_networkConnection = networkConnection;
			_pipelineManager = networkConnection.PipelineManager;
			_mappingNetwork = networkConnection.MappingNetwork;
			SpoolerTimer = new Timer(PauseBetweenTransmissionOnTheNode) { AutoReset = true };
			SpoolerTimer.Elapsed += (sender, e) => DataDelivery();
		}
		internal readonly Timer SpoolerTimer;
		private readonly NetworkConnection _networkConnection;
		private readonly PipelineManager _pipelineManager;
		private readonly MappingNetwork _mappingNetwork;
		internal static readonly int PauseBetweenTransmissionOnTheNode = 2000;
		/// <summary>
		/// Memorize when the last communication was made at a certain level
		/// </summary>
		private readonly Dictionary<int, DateTime> _lastTransmission = new Dictionary<int, DateTime>();

		internal void DataDelivery()
		{
			List<Node> level0Connections = null; //The transmissions at this level will receive the signature of the timestamp from the node that receives them, these signatures once received all must be sent to every single node of this level
			var listOfObjForNodes = new Dictionary<Node, List<ObjToNode>>();
			var toLevels = new List<int>();
			lock (_networkConnection.PipelineManager.Pipeline)
				_networkConnection.PipelineManager.Pipeline.ForEach(element => toLevels.AddRange(element.Levels.FindAll(x => !toLevels.Contains(x))));
			toLevels.Sort();
			foreach (var toLevel in toLevels)
			{
				var msFromLastTransmissionAtThisLevel = int.MaxValue;
				lock (_lastTransmission)
					if (_lastTransmission.TryGetValue(toLevel, out var transmissionTime))
						msFromLastTransmissionAtThisLevel = (int)(DateTime.UtcNow - transmissionTime).TotalMilliseconds;
				if (msFromLastTransmissionAtThisLevel <= PauseBetweenTransmissionOnTheNode) continue;
				var connections = _networkConnection.MappingNetwork.GetConnections(toLevel); // ok, level is base 1
				if (toLevel == 1) //I'm at level 0 and broadcast at level 1
					level0Connections = connections;
				lock (_networkConnection.PipelineManager.Pipeline)
					foreach (var elementPipeline in _networkConnection.PipelineManager.Pipeline)
						lock (elementPipeline.SendedLevel)
							if (elementPipeline.Levels.Contains(toLevel) && !elementPipeline.SendedLevel.Contains(toLevel))
							{
								elementPipeline.SendedLevel.Add(toLevel);
								var elementToNode = new ObjToNode(elementPipeline.Element) { Level = toLevel };
								if (toLevel == 1 && elementToNode.Timestamp == 0) //I'm at level 0 and broadcast at level 1      
																																	// We assign the timestamp and sign it
																																	// The nodes of level 1 that will receive this element, will verify the timestamp and if congruous they sign it and return the signature in response to the forwarding.
									elementToNode.AddFirstTimestamp(_networkConnection.MyNode, _networkConnection.Now.Ticks);
								lock (elementPipeline.ExcludeNodes)
									foreach (var node in connections)
										if (!elementPipeline.ExcludeNodes.Contains(node))
										{
											elementPipeline.ExcludeNodes.Add(node);
											if (!listOfObjForNodes.TryGetValue(node, out var toSendToNode))
											{
												toSendToNode = new List<ObjToNode>();
												listOfObjForNodes.Add(node, toSendToNode);
											}
											toSendToNode.Add(elementToNode);
											lock (_lastTransmission)
											{
												_lastTransmission.Remove(toLevel);
												_lastTransmission.Add(toLevel, DateTime.UtcNow);
											}

										}
							}

			}
			var responseMonitorForLevel0 = new Protocol.ResponseMonitor { Level0Connections = level0Connections };
			foreach (var toSend in listOfObjForNodes)
			{
				if (level0Connections != null && level0Connections.Contains(toSend.Key))
				{
					if (toSend.Value.Find(x => x.TimestampSignature == null) != null)
					{
						Debugger.Break();
					}
					_networkConnection.Protocol.SendElementsToNode(toSend.Value, toSend.Key, responseMonitorForLevel0);
				}
				else
					_networkConnection.Protocol.SendElementsToNode(toSend.Value, toSend.Key);
			}
		}
	}

}
