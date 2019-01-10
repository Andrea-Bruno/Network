using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using static NetworkManager.NetworkConnection;

namespace NetworkManager
{
	/// <summary>
	/// The pipeline is a mechanism that allows you to distribute objects on the networkConnection and provides for the assignment of a decentralized timestamp.
	/// The objects inserted in the pipeline will exit from it on all the nodes following the chronological order of input(they come out sorted by timestamp).
	/// The data output from the pipeline must be managed by actions that are programmed when the networkConnection is initialized.
	/// </summary>
	internal class PipelineManager
	{
		public PipelineManager(NetworkConnection networkConnection)
		{
			_networkConnection = networkConnection;
			_spooler = new Spooler(networkConnection);
			var newStats = new Timer(43200000) { AutoReset = true, Enabled = true };
			newStats.Elapsed += (sender, e) => NewStatsElapsed();
			Pipeline = new PipelineBuffer(_networkConnection, Scheduler, _spooler);
		}

		private readonly NetworkConnection _networkConnection;
		/// <summary>
		/// The spooler contains the logic that allows synchronization of data on the peer2peer networkConnection
		/// </summary>
		private readonly Spooler _spooler;

		/// <summary>
		/// Send an object to the networkConnection to be inserted in the shared pipeline
		/// </summary>
		/// <param name="Object">Object to send</param>
		/// <returns></returns>
		public Protocol.StandardAnswer AddToSharedPipeline(object Object)
		{
			return _networkConnection.Protocol.AddToSharedPipeline(_networkConnection.GetRandomNode(), Object);
		}

		private void Scheduler()
		{
			//var Pipeline = PipelineManager.Pipeline;
			lock (Pipeline)
			{
				var NetworkSyncTimeSpan = MappingNetwork.NetworkSyncTimeSpan(_networkConnection.NodeList.Count);
				var toRemove = new List<ElementPipeline>();
				var thisTime = _networkConnection.Now;
				foreach (var item in Pipeline)
				{
					//For objects of level 0 the timestamp is added by the spooler at the time of forwarding in order to reduce the time difference between the timestamp and the reception of the object on the other node
					if (item.Element.Timestamp == 0) continue;
					if ((thisTime - new DateTime(item.Element.Timestamp)) > NetworkSyncTimeSpan)
					{
						toRemove.Add(item);
						var objectName = Utility.GetObjectName(item.Element.XmlObject);
						OnReceiveObjectFromPipeline(objectName, item.Element.XmlObject, item.Element.Timestamp);
					}
					else
						break;//because the pipeline is sorted by Timespan
				}
				Pipeline.Remove(toRemove);
			}
		}
		private void OnReceiveObjectFromPipeline(string objectName, string xmlObject, long timestamp)
		{
			if (objectName == "NodeOnlineNotification")
				ReceiveNodeOnlineNotification(xmlObject, timestamp);
			else if (objectName == "NodeOfflineNotification")
				ReceiveNodeOfflineNotification(xmlObject);
			if (_pipelineCompletedAction.TryGetValue(objectName, out var action))
				action.Invoke(xmlObject, timestamp);
		}

		private void ReceiveNodeOfflineNotification(string xmlObject)
		{
			if (Converter.XmlToObject(xmlObject, typeof(Protocol.NodeOfflineNotification), out var obj))
			{
				var nodeOfflineNotification = (Protocol.NodeOfflineNotification)(obj);
				var node = _networkConnection.NodeList.ListWithComingSoon().Find(x => x.Ip == nodeOfflineNotification.Ip);
				if (node != null && nodeOfflineNotification.ValidateSignature(node.Rsa))
					_networkConnection.NodeList.Remove(node, new DateTime(nodeOfflineNotification.atTimestamp));
			}
		}

		private void ReceiveNodeOnlineNotification(string xmlObject, long timestamp)
		{
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
						var nodeOfSignature = _networkConnection.NodeList.ListWithRecentNodes().Find(x => x.Ip == item.NodeIp);
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
						if (_networkConnection.ValidateConnectionAtLevel0(nodeAtLevel0, connections))
							_networkConnection.NodeList.Add(nodeOnlineNotification.Node, timestamp);
				}
			}
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
		/// In this case this will be the node at zero level
		/// </summary>
		/// <param name="xmlObject">Serialized object im format xml</param>
		/// <returns></returns>
		internal bool AddLocal(string xmlObject)
		{
			if (string.IsNullOrEmpty(xmlObject))
				return false;
			//long Timestamp = NetworkConnection.Now.Ticks;
			//var Element = new Element() { Timestamp = Timestamp, XmlObject = XmlObject };
			//At level 0 the timestamp will be assigned before transmission to the node in order to reduce the difference with the timestamp on the node
			try
			{
				var element = new Element() { XmlObject = xmlObject };
				var elementPipeline = new ElementPipeline(element);
				elementPipeline.Levels.Add(1); //Destination level
				Pipeline.Add(elementPipeline);
				return true;
			}
			catch { return true; }
		}
		internal bool RemoveLocal(Element element)
		{
#if DEBUG
			Debugger.Break();
#endif
			var elementPipeline = Pipeline.Find(e => e.Element == element);
			if (elementPipeline == null)
				return false;
			Pipeline.Remove(elementPipeline);
			return true;
		}

		/// <summary>
		/// In this waiting list all the objects awaiting the timestamp signature are inserted by all the nodes assigned to the first level distribution
		/// </summary>
		private readonly List<ObjToNode> _standByList = new List<ObjToNode>();

		/// <summary>
		/// Insert the objects in the local pipeline to be synchronized
		/// The objectsFromNode come from other nodes
		/// </summary>
		/// <param name="objectsFromNode">Elements come from other nodes</param>
		/// <param name="fromNode">From which node comes the element</param>
		internal ObjToNode.TimestampVector AddLocalFromNode(IEnumerable<ObjToNode> objectsFromNode, Node fromNode)
		{
			ObjToNode.TimestampVector result = null;
			lock (Pipeline)
			{
				var networkSyncTimeSpan = MappingNetwork.NetworkSyncTimeSpan(_networkConnection.NodeList.Count);
				var addList = new List<ElementPipeline>();
				var thisTime = _networkConnection.Now;
				// Remove any objects in standby that have not received the timestamp signed by everyone
				lock (_standByList) _standByList.FindAll(o => (thisTime - new DateTime(o.Timestamp)).TotalSeconds >= 5).ForEach(o => _standByList.Remove(o));
				foreach (var objFromNode in objectsFromNode)
				{
					var timePassedFromInsertion = thisTime - new DateTime(objFromNode.Timestamp);
					UpdateStats(timePassedFromInsertion);
					if (timePassedFromInsertion <= networkSyncTimeSpan)
					{
						if (objFromNode.Level == 1)
						{
							UpdateStats(timePassedFromInsertion, true);
							lock (_standByList)
							{
								// You received an object from level 0
								// This is an object that is just inserted, so you must certify the timestamp and send the certificate to the node that took delivery of the object.
								// The object must then be put on standby until the node sends all the certificates for the timestamp.
								if (objFromNode.CheckNodeThatStartedDistributingTheObject(fromNode))
								{
									var signature = objFromNode.CreateTheSignatureForTheTimestamp(_networkConnection.MyNode, _networkConnection.Now);
#if DEBUG
									if (signature == null) Debugger.Break();
#endif
									_standByList.Add(objFromNode);
									if (result == null) result = new ObjToNode.TimestampVector();
									result.Add(objFromNode.ShortHash(), signature);
								}
								else
								{
									Utility.Log("security", "check failure fromNode " + fromNode.Ip);
									Debugger.Break();
								}
							}
						}
						else
						{
							var elementPipeline = Pipeline.Find(x => x.Element.Timestamp == objFromNode.Timestamp && x.Element.XmlObject == objFromNode.XmlObject);
							if (elementPipeline != null)
							{
								var level = objFromNode.Level + 1;
								lock (elementPipeline.Levels)
									if (!elementPipeline.Levels.Contains(level))
										elementPipeline.Levels.Add(level);
							}
							else
							{
								UpdateStats(timePassedFromInsertion, true);
								var CheckSignature = objFromNode.CheckSignedTimestamp(_networkConnection, fromNode.Ip);
								if (CheckSignature != ObjToNode.CheckSignedTimestampResult.Ok)
									Debugger.Break();
								else
								{
#if DEBUG
									var duplicate = Pipeline.Find(x => x.Element.XmlObject == objFromNode.XmlObject);
									if (duplicate != null) Debugger.Break();
#endif
									elementPipeline = new ElementPipeline(objFromNode);
									addList.Add(elementPipeline);
								}
							}
							if (elementPipeline != null)
							{
								lock (elementPipeline.ExcludeNodes)
									elementPipeline.ExcludeNodes.Add(fromNode);
								elementPipeline.Received++;
							}
						}
					}
					else
					{
						//A dishonest node has started a fork through a fake timestamp?
						Stats24H.ElementsArrivedOutOfTime++;
						_stats12H.ElementsArrivedOutOfTime++;
					}
				}
				Pipeline.Add(addList);
			}
			return result;
		}
		internal const double SignatureTimeout = 2.0; //*** Node at level 0 have max N second to transmit the signedTimestamps

		/// <summary>
		/// The node at level 1 received the signatures collected from the node at level 0. Check if everything is correct, remove the objects from stand by and start to distributing them 
		/// </summary>
		/// <param name="signedTimestamps">The decentralized signature</param>
		/// <param name="fromIp">The IP of the node at level 0 that transmitted the signedTimestamps</param>
		/// <returns></returns>
		internal bool UnlockElementsInStandBy(ObjToNode.TimestampVector signedTimestamps, uint fromIp)
		{
			var timeLimit = _networkConnection.Now.AddSeconds(-SignatureTimeout).Ticks; // Node at level 0 have max N second to transmit the signedTimestamps
			bool result = true;
			lock (Pipeline)
			{
				var remove = new List<ObjToNode>();
				var addList = new List<ElementPipeline>();
				lock (_standByList)
				{
					foreach (var objFromNode in _standByList)
						if (signedTimestamps.TryGetValue(objFromNode.ShortHash(), out var signature))
						{
							remove.Add(objFromNode);
							if (objFromNode.Timestamp > timeLimit) // Prevents the node at level 0 from holding the data for a long time, then distributing it late and attempting dishonest distributions
							{
								objFromNode.TimestampSignature = signature;
								var check = objFromNode.CheckSignedTimestamp(_networkConnection, fromIp, true);
								if (check == ObjToNode.CheckSignedTimestampResult.Ok)
								{
									addList.Add(new ElementPipeline(objFromNode));
								}
								else
								{
									result = false;
									Debugger.Break();
								}
							}
							else
							{
								result = false;
								Debugger.Break();
							}
						}
						else
						{
							result = false;
							Debugger.Break();
						}
					foreach (var item in remove)
						_standByList.Remove(item);
				}
				Pipeline.Add(addList);
			}
			return result;
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

			internal int ReceivedElements;
			internal int ReceivedUnivocalElements;
			internal int ElementsArrivedOutOfTime;
			internal TimeSpan MaximumArrivalTimeElement; // Maximum time to update the node (from the first node)
			internal TimeSpan NetworkLatency; // Maximum time to update the node (from all node)
		}

		internal readonly PipelineBuffer Pipeline;
		internal class ElementPipeline
		{
			/// <summary>
			/// Used only from the node at level 0.
			/// In this case we still do not have the joint signature on the timestamp, and at this stage the timestamp still has to be assigned
			/// </summary>
			/// <param name="element"></param>
			public ElementPipeline(Element element)
			{
				Element = element;
			}
			public ElementPipeline(ObjToNode objFromNode)
			{
				lock (Levels)
					if (!Levels.Contains(objFromNode.Level + 1))
						Levels.Add(objFromNode.Level + 1);
				Element = objFromNode.GetElement;
				//TimestampSignature = objFromNode.TimestampSignature;
			}
			public Element Element;
			public List<int> SendedLevel = new List<int>();
			public List<Node> ExcludeNodes = new List<Node>();
			public int Received;
			/// <summary>
			/// Target levels
			/// </summary>
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
			// _spooler.SpoolerTimer : This timer is responsible for distributing the objectsFromNode on the nodes
			private NetworkConnection _networkConnection;
			private Timer _pipelineTimer; //This timer is responsible for getting the objectsFromNode out of the pipeline
			private Spooler _spooler;
			public PipelineBuffer(NetworkConnection networkConnection, Action scheduler, Spooler spooler)
			{
				_networkConnection = networkConnection;
				_spooler = spooler;
				_pipelineTimer = new Timer() { AutoReset = true };//***
				_pipelineTimer.Elapsed += (sender, e) => scheduler();
			}
			public new void Add(ElementPipeline item)
			{
				lock (this)
					base.Add(item);
				Sort();
			}

			public void Add(List<ElementPipeline> elements)
			{
				if (elements.Count != 0)
				{
					lock (this)
						foreach (var element in elements)
							base.Add(element);
					Sort();
				}
			}

			public new void Sort()
			{
				// This part of the code solves the problem of the "Byzantine fault tolerance"
				lock (this)
				{
					Sort((x, y) =>
					{
						int compare = x.Element.Timestamp.CompareTo(y.Element.Timestamp);
						if (compare != 0)
							return compare;
						return x.Element.XmlObject.CompareTo(y.Element.XmlObject);
					});
				}
				_networkConnection.PipelineElementsChanged(Count);
				if (_spooler.SpoolerTimer.Enabled == false)
					_spooler.SpoolerTimer.Start();
				_spooler.DataDelivery();
				PlanNextSchedulerRun();
			}

			private void PlanNextSchedulerRun()
			{
				_pipelineTimer.Enabled = false;
				ElementPipeline finded;
				lock (this)
					finded = Find(x => x.Element.Timestamp != 0);
				if (finded != null)
				{
					var NetworkSyncTimeSpan = MappingNetwork.NetworkSyncTimeSpan(_networkConnection.NodeList.Count);
					var exitFromPipelineTime = new DateTime(finded.Element.Timestamp).Add(NetworkSyncTimeSpan);
					var remainingTime = (exitFromPipelineTime - _networkConnection.Now).TotalMilliseconds;
					if (remainingTime < 0) remainingTime = 0;
					_pipelineTimer.Interval = remainingTime + 1;
					_pipelineTimer.Start();
				}
			}

			public new void Remove(ElementPipeline item)
			{
				lock (this)
					base.Remove(item);
				_networkConnection.PipelineElementsChanged(Count);
				if (Count != 0) return;
				_pipelineTimer.Stop();
				_spooler.SpoolerTimer.Stop();
			}
			public void Remove(List<ElementPipeline> elements)
			{
				if (elements.Count != 0)
				{
					lock (this)
						foreach (var element in elements)
							base.Remove(element);
					_networkConnection.PipelineElementsChanged(Count);
					if (Count != 0) return;
					_pipelineTimer.Stop();
					_spooler.SpoolerTimer.Stop();
				}
			}
		}

	}



}
