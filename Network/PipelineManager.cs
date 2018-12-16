using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using static NetworkManager.NetworkConnection;

namespace NetworkManager
{
	/// <summary>
	/// The pipeline is a mechanism that allows you to distribute objects on the networkConnection by assigning a timestamp.
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
			//foreach (SyncData Action in PipelineCompletedAction)
			//  Action.Invoke(item.XmlObject, item.Timestamp);
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
						var nodeOfSignature = _networkConnection.NodeList.CurrentAndRecentNodes().Find(x => x.Ip == item.NodeIp);
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
			var element = new Element() { XmlObject = xmlObject };
			var elementPipeline = new ElementPipeline(element);
			elementPipeline.Levels.Add(1);
			Pipeline.Add(elementPipeline);
			return true;
		}
		internal bool RemoveLocal(Element element)
		{
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
		/// The elements come from other nodes
		/// </summary>
		/// <param name="elements">Elements come from other nodes</param>
		/// <param name="fromNode">From which node comes the element</param>
		internal ObjToNode.TimestampVector AddLocalFromNode(IEnumerable<ObjToNode> elements, Node fromNode)
		{
			ObjToNode.TimestampVector result = null;
			lock (Pipeline)
			{
				var networkSyncTimeSpan = MappingNetwork.NetworkSyncTimeSpan(_networkConnection.NodeList.Count);
				var addList = new List<ElementPipeline>();
				var thisTime = _networkConnection.Now;
				// Remove any objects in standby that have not received the timestamp signed by everyone
				lock (_standByList) _standByList.FindAll(o => (thisTime - new DateTime(o.Timestamp)).TotalSeconds >= 5).ForEach(o => _standByList.Remove(o));
				foreach (var objToNode in elements)
				{
					var timePassedFromInsertion = thisTime - new DateTime(objToNode.Timestamp);
					UpdateStats(timePassedFromInsertion);
					if (timePassedFromInsertion <= networkSyncTimeSpan)
					{
						if (objToNode.Level == 1)
						{
							UpdateStats(timePassedFromInsertion, true);
							lock (_standByList)
							{
								// You received an object from level 0
								// This is an object that is just inserted, so you must certify the timestamp and send the certificate to the node that took delivery of the object.
								// The object must then be put on standby until the node sends all the certificates for the timestamp.
								if (objToNode.CheckNodeThatStartedDistributingTheObject(fromNode))
								{
									var signature = objToNode.CreateTheSignatureForTheTimestamp(_networkConnection.MyNode, _networkConnection.Now);
#if DEBUG
									if (signature == null) Debugger.Break();
#endif
									_standByList.Add(objToNode);
									if (result == null) result = new ObjToNode.TimestampVector();
									result.Add(objToNode.ShortHash(), signature);
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
							var level = objToNode.Level + 1;
							var elementPipeline = Pipeline.Find(x => x.Element.Timestamp == objToNode.Timestamp && x.Element.XmlObject == objToNode.XmlObject);
							if (elementPipeline == null)
							{
								UpdateStats(timePassedFromInsertion, true);
								elementPipeline = new ElementPipeline(objToNode);
								addList.Add(elementPipeline);
							}
							//else if (elementPipeline.TimestampSignature == null && objToNode.TimestampSignature != null)
							//	elementPipeline.TimestampSignature = objToNode.TimestampSignature;
							lock (elementPipeline.Levels)
								if (elementPipeline.Levels.Contains(level))
									elementPipeline.Levels.Add(level);
							lock (elementPipeline.ExcludeNodes)
								elementPipeline.ExcludeNodes.Add(fromNode);
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
					foreach (var objToNode in _standByList)
						if (signedTimestamps.TryGetValue(objToNode.ShortHash(), out var signature))
						{
							remove.Add(objToNode);
							if (objToNode.Timestamp > timeLimit) // Prevents the node at level 0 from holding the data for a long time, then distributing it late and attempting dishonest distributions
							{
								objToNode.TimestampSignature = signature;
								var check = objToNode.CheckSignedTimestamp(_networkConnection, fromIp);
								if (check == ObjToNode.CheckSignedTimestampResult.Ok)
								{
									addList.Add(new ElementPipeline(objToNode));
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
							}
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
			public ElementPipeline(ObjToNode objToNode)
			{
				Element = objToNode.GetElement;
				TimestampSignature = objToNode.TimestampSignature;
			}
			public Element Element;
			public List<int> SendedLevel = new List<int>();
			public List<Node> ExcludeNodes = new List<Node>();
			public int Received;
			/// <summary>
			/// Do not worry: it's a joint signature, its data includes the node that put the signature and the calculation is done on the hash of the timestamp + xml of the element
			/// </summary>
			public string TimestampSignature;
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
			// _spooler.SpoolerTimer : This timer is responsible for distributing the elements on the nodes
			private NetworkConnection _networkConnection;
			private Timer _pipelineTimer; //This timer is responsible for getting the elements out of the pipeline
			private Spooler _spooler;
			public PipelineBuffer(NetworkConnection networkConnection, Action scheduler, Spooler spooler)
			{
				_networkConnection = networkConnection;
				_spooler = spooler;
				_pipelineTimer = new Timer(1000) { AutoReset = true };//***
				_pipelineTimer.Elapsed += (sender, e) => scheduler();
			}
			public new void Add(ElementPipeline item)
			{
				lock (this)
					base.Add(item);
				Sort();
				if (_pipelineTimer.Enabled == false)
					_pipelineTimer.Start();
				if (_spooler.SpoolerTimer.Enabled == false)
					_spooler.SpoolerTimer.Start();
				_networkConnection.PipelineElementsChanged(Count);
				_spooler.DataDelivery();
			}

			public void Add(List<ElementPipeline> elements)
			{
				if (elements.Count != 0)
				{
					lock (this)
						foreach (var element in elements)
							base.Add(element);
					Sort();
					if (_pipelineTimer.Enabled == false)
						_pipelineTimer.Start();
					if (_spooler.SpoolerTimer.Enabled == false)
						_spooler.SpoolerTimer.Start();
					_networkConnection.PipelineElementsChanged(Count);
					_spooler.DataDelivery();
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
			public new void Sort()
			{
				lock (this)
				{
					var sorted = this.OrderBy(x => x.Element.XmlObject).ToList();//Used for the element whit same Timestamp
					sorted = sorted.OrderBy(x => x.Element.Timestamp).ToList();
					Clear();
					AddRange(sorted);
				}
			}
		}

	}



}
