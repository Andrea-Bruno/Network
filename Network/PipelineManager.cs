using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Timers;
using static NetworkManager.Network;

namespace NetworkManager
{
	/// <summary>
	/// The pipeline is a mechanism that allows you to distribute objects on the network by assigning a timestamp.
	/// The objects inserted in the pipeline will exit from it on all the nodes following the chronological order of input(they come out sorted by timestamp).
	/// The data output from the pipeline must be managed by actions that are programmed when the network is initialized.
	/// </summary>
	internal class PipelineManager
	{
		public PipelineManager(Network network)
		{
			_network = network;
			_spooler = new Spooler(network);
			var newStats = new Timer(43200000) { AutoReset = true, Enabled = true };
			newStats.Elapsed += (sender, e) => NewStatsElapsed();
			Pipeline = new PipelineBuffer(Scheduler, _spooler.SpoolerTimer);
		}

		private readonly Network _network;
		/// <summary>
		/// The spooler contains the logic that allows synchronization of data on the peer2peer network
		/// </summary>
		private readonly Spooler _spooler;

		/// <summary>
		/// Send an object to the network to be inserted in the shared pipeline
		/// </summary>
		/// <param name="Object">Object to send</param>
		/// <returns></returns>
		public Protocol.StandardAnswer AddToSharedPipeline(object Object)
		{
			return _network.Protocol.AddToSharedPipeline(_network.GetRandomNode(), Object);
		}

		private void Scheduler()
		{
			//var Pipeline = PipelineManager.Pipeline;
			lock (Pipeline)
			{
				var toRemove = new List<ElementPipeline>();
				var thisTime = _network.Now;
				foreach (var item in Pipeline)
				{
					//For objects of level 0 the timestamp is added by the spooler at the time of forwarding in order to reduce the time difference between the timestamp and the reception of the object on the other node
					if (item.Element.Timestamp == 0) continue;
					if ((thisTime - new DateTime(item.Element.Timestamp)) > _network.MappingNetwork.NetworkSyncTimeSpan)
					{
						toRemove.Add(item);
						var objectName = Utility.GetObjectName(item.Element.XmlObject);
						OnReceiveObjectFromPipeline(objectName, item.Element.XmlObject, item.Element.Timestamp);
					}
					else
						break;//because the pipeline is sorted by Timespan
				}
				foreach (var item in toRemove)
					Pipeline.Remove(item);
			}
		}
		private void OnReceiveObjectFromPipeline(string objectName, string xmlObject, long timestamp)
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

			if (_pipelineCompletedAction.TryGetValue(objectName, out var action))
				action.Invoke(xmlObject, timestamp);
			//foreach (SyncData Action in PipelineCompletedAction)
			//  Action.Invoke(item.XmlObject, item.Timestamp);
		}
		/// <summary>
		/// Insert the object in the local pipeline to be synchronized
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
		/// Insert the object in the local pipeline to be synchronized
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
			lock (Pipeline)
			{
				var elementPipeline = new ElementPipeline(element);
				elementPipeline.Levels.Add(1);
				Pipeline.Add(elementPipeline);
				Pipeline.Sort();
			}
			_spooler.DataDelivery();
			return true;
		}

		/// <summary>
		/// In this waiting list all the objects awaiting the timestamp signature are inserted by all the nodes assigned to the first level distribution
		/// </summary>
		private readonly List<ObjToNode> _standByList = new List<ObjToNode>();

		/// <summary>
		/// Insert the objects in the local pipeline to be synchronized
		/// The elements come from other nodes
		/// </summary>
		/// <param name="elements">Elements come from other nodes</param>
		/// <param name="fromNode">From which node comes the element</param>
		internal ObjToNode.TimestampVector AddLocalFromNode(IEnumerable<ObjToNode> elements, Node fromNode)
		{
			ObjToNode.TimestampVector result = null;
			lock (Pipeline)
			{
				var count = Pipeline.Count;
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
								result.SignedTimestamp.Add(objToNode.ShortHash(), signature);
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
							var elementPipeline = Pipeline.Find(x => x.Element.Timestamp == objToNode.Timestamp && x.Element.XmlObject == objToNode.XmlObject);
							if (elementPipeline == null)
							{
								UpdateStats(timePassedFromInsertion, true);
								elementPipeline = new ElementPipeline(objToNode.GetElement);
								Pipeline.Add(elementPipeline);
							}
							lock (elementPipeline.Levels)
								if (elementPipeline.Levels.Contains(level))
									elementPipeline.Levels.Add(level);
							lock (elementPipeline.SendedNode)
								elementPipeline.SendedNode.Add(fromNode);
							elementPipeline.Received++;
						}
					}
					else
					{
						//A dishonest node has started a fork through a fake timestamp?
						Stats24H.ElementsArrivedOutOfTime++;
						_stats12H.ElementsArrivedOutOfTime++;
					}
				}
				if (count == Pipeline.Count) return result;
				Pipeline.Sort();
				_spooler.DataDelivery();
			}
			return result;
		}
		internal bool UnlockElementsInStandBy(ObjToNode.TimestampVector signedTimestamps, Node fromNode)
		{
			lock (Pipeline)
			{
				var count = Pipeline.Count();
				var remove = new List<ObjToNode>();
				lock (_standByList)
				{
					foreach (var objToNode in _standByList)
						if (signedTimestamps.SignedTimestamp.TryGetValue(objToNode.ShortHash(), out var signatures))
						{
							remove.Add(objToNode);
							objToNode.TimestampSignature = signatures;
							if (objToNode.CheckSignedTimestamp(_network) == ObjToNode.CheckSignedTimestampResult.Ok)
							{
								Pipeline.Add(new ElementPipeline(objToNode.GetElement));
							}
						}
					foreach (var item in remove)
						_standByList.Remove(item);
				}
				if (count == Pipeline.Count()) return true;
				Pipeline.Sort();
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

		internal readonly PipelineBuffer Pipeline;
		internal class ElementPipeline
		{
			public ElementPipeline(Element element)
			{
				Element = element;
			}
			public Element Element;
			public List<Node> SendedNode = new List<Node>();
			public int Received;
			public List<int> Levels = new List<int>();
		}
		/// <summary>
		/// Add a action used to local sync the objects coming from the pipeline
		/// </summary>
		/// <param name="action">Action to execute for every object</param>
		/// <param name="forObjectName">Indicates what kind of objects will be treated by this action</param>
		public bool AddSyncDataAction(SyncData action, string forObjectName)
		{
			if (_pipelineCompletedAction.ContainsKey(forObjectName))
				return false;
			_pipelineCompletedAction.Add(forObjectName, action);
			return true;
		}
		private readonly Dictionary<string, SyncData> _pipelineCompletedAction = new Dictionary<string, SyncData>();

		internal class PipelineBuffer : List<ElementPipeline>
		{
			private readonly Timer _pipelineTimer; //This timer is responsible for getting the elements out of the pipeline
			private readonly Timer _spoolTimer; //This timer is responsible for distributing the elements on the nodes
			public PipelineBuffer(Action scheduler, Timer spoolerTimer)
			{
				_spoolTimer = spoolerTimer;
				_pipelineTimer = new Timer(1000) { AutoReset = true };//***
				_pipelineTimer.Elapsed += (sender, e) => scheduler();
			}
			public new void Add(ElementPipeline item)
			{
				base.Add(item);
				if (_pipelineTimer.Enabled == false)
					_pipelineTimer.Start();
				if (_spoolTimer.Enabled == false)
					_spoolTimer.Start();
			}
			public new void Remove(ElementPipeline item)
			{
				base.Remove(item);
				if (this.Count != 0) return;
				_pipelineTimer.Stop();
				_spoolTimer.Stop();
			}
			public new void Sort()
			{
				var sorted = this.OrderBy(x => x.Element.XmlObject).ToList();//Used for the element whit same Timestamp
				sorted = sorted.OrderBy(x => x.Element.Timestamp).ToList();
				base.Clear();
				base.AddRange(sorted);
			}

		}

	}



}
