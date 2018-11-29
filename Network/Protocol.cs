using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace NetworkManager
{
	public class Protocol
	{
		public delegate object GetObject(string xmlObject);

		public enum StandardAnswer
		{
			Ok,
			Disconnected,
			Error,
			DuplicateIp,
			TooSlow,
			Failure,
			NoAnswer,
			Declined,
			IpError,
			Unauthorized,
			UnexpectedAnswer
		}

		private StandardAnswer Answer(string xmlResult)
		{
			if (string.IsNullOrEmpty(xmlResult)) return StandardAnswer.NoAnswer;
			if (Utility.GetObjectName(xmlResult) != "StandardAnswer") return StandardAnswer.UnexpectedAnswer;
			try
			{
				Converter.XmlToObject(xmlResult, typeof(StandardAnswer), out var returnObj);
				var answer = (StandardAnswer)returnObj;
				return answer;
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				return StandardAnswer.Failure;
			}
		}

		private const int DefaultSpeedLimit = 1000;//***
		private readonly int _speedLimit = DefaultSpeedLimit;
		private readonly NetworkConnection _networkConnection;
		internal readonly Dictionary<string, GetObject> OnReceivingObjectsActions = new Dictionary<string, GetObject>();
		internal readonly Dictionary<string, GetObject> OnRequestActions = new Dictionary<string, GetObject>();

		internal Protocol(NetworkConnection networkConnection)
		{
			_networkConnection = networkConnection;
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
				xmlResult = _networkConnection.Communication.GetObjectSync(toNode.Address, request, obj, toNode.MachineName + ".");
			} while (string.IsNullOrEmpty(xmlResult) && Try <= 10);
			if (Try <= 10) return xmlResult;
			Debugger.Break();
			_networkConnection.IsOnline = false;
			_networkConnection.OnlineDetection.WaitForInternetConnection();

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
			Converter.XmlToObject(xmlResult, typeof(List<Node>), out var returnObj);
			var nodeList = (List<Node>)returnObj;
			return nodeList;
		}

		internal StandardAnswer ImOffline(Node toNode, Node myNode)
		{
			return Answer(SendRequest(toNode, StandardMessages.ImOffline, myNode));
		}

		internal StandardAnswer ImOnline(Node toNode, Node myNode)
		{
			return Answer(SendRequest(toNode, StandardMessages.ImOnline, myNode));
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
				Converter.XmlToObject(xmlResult, typeof(Stats), out var returnObj);
				stats = (Stats)returnObj;
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
			var connections = _networkConnection.MappingNetwork.GetConnections(1);
			var speedSigned = SpeedTestSigned(nodeToTesting);
			speedTestResults.Add(speedSigned);
			var speeds = new List<int> { speedSigned.Speed };
			if (connections.Count != 0)
				foreach (var node in connections)
				{
					var xmlResult = SendRequest(node, StandardMessages.RequestTestSpeed, nodeToTesting);
					if (!string.IsNullOrEmpty(xmlResult))
						try
						{
							Converter.XmlToObject(xmlResult, typeof(SpeedTestResult), out var returnObj);
							var result = (SpeedTestResult)returnObj;
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
			var best = speeds[0]; //if best = -1 = failure speed test
			return best != -1 && best <= _speedLimit;
		}

		internal SpeedTestResult SpeedTestSigned(Node nodeToTesting)
		{
			var result = new SpeedTestResult { Speed = SpeedTest(nodeToTesting), NodeIp = _networkConnection.MyNode.Ip };
			result.SignTheResult(_networkConnection.MyNode, nodeToTesting.Ip, _networkConnection.Now.Ticks);
			return result;
		}

		private int SpeedTest(Node nodeToTesting)
		{
			var start = DateTime.UtcNow;
			for (var i = 0; i < 10; i++)
			{
				var xmlResult = SendRequest(nodeToTesting, StandardMessages.TestSpeed);
				if (xmlResult != null && xmlResult.Length == 131111) continue;
				Debugger.Break();
				return -1; //failure speed test
			}
			var speed = (int)(DateTime.UtcNow - start).TotalMilliseconds;
			return speed;
		}

		/// <summary>
		/// If the decentralized speed test is passed, then it propagates the notification on all the nodes that there is a new online node.
		/// </summary>
		/// <param name="node">The new node that has just been added</param>
		/// <param name="speedTestResults">The certified result of the speed test</param>
		/// <returns>Positive result if return true</returns>
		internal bool NotificationNewNodeIsOnline(Node node, List<SpeedTestResult> speedTestResults)
		{
			var notification = new NodeOnlineNotification { Node = node, Signatures = speedTestResults };
			return _networkConnection.PipelineManager.AddLocal(notification);
		}

		internal StandardAnswer AddToSharedPipeline(Node toNode, object Object)
		{
			if (_networkConnection.ThisNode.ConnectionStatus != StandardAnswer.Ok)
				return _networkConnection.ThisNode.ConnectionStatus;
			try
			{
				return Answer(SendRequest(toNode, StandardMessages.AddToPipeline, Object));
			}
			catch (Exception ex)
			{
				Debug.Print(ex.Message);
				Debugger.Break();
				return StandardAnswer.Error;
			}
		}

		/// <summary>
		///   It transfers a list of elements to a node, if this is the node at level 0, it means that these elements have just
		///   been taken into charge, it will then be distributed to all connections at level 0, collect all the signatures that
		///   certify the timestamp, and send the signatures to the nodes connected to level 0.
		///   This procedure is used to create a decentralized timestamp within the networkConnection.
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
				if (!_networkConnection.NodeList.Contains(toNode)) return;
				xmlResult = SendRequest(toNode, StandardMessages.SendElementsToNode, elements);
				var objectName = Utility.GetObjectName(xmlResult);
				if (objectName == "TimestampVector")
				{
					var answer = Answer(xmlResult);
					if (answer != StandardAnswer.Ok)
					{
						// Error occurred
					}
				}
				if (responseMonitor == null) return;
				if (objectName == "TimestampVector")
				{
					if (Converter.XmlToObject(xmlResult, typeof(ObjToNode.TimestampVector), out var objTimestampVector))
					{
						var timestamps = (ObjToNode.TimestampVector)objTimestampVector;
						foreach (var element in elements)
							if (element.Level == 1 && timestamps.TryGetValue(element.ShortHash(), out var signedTimestamp))
								element.TimestampSignature += signedTimestamp;
					}
				}
				else
				{
					var answer = Answer(xmlResult);
					// Add the response management here!!!
				}
				responseMonitor.ResponseCounter += 1;
				if (responseMonitor.ResponseCounter != responseMonitor.Level0Connections.Count) return;
				// All nodes connected to the zero level have signed the timestamp, now the signature of the timestamp of all the nodes must be sent to every single node.
				// This operation is used to create a decentralized timestamp.
				var timestampVector = new ObjToNode.TimestampVector();
				foreach (var element in elements)
					timestampVector.Add(element.ShortHash(), element.TimestampSignature);
				foreach (var node in responseMonitor.Level0Connections)
					// The node at zero level (the entry point of the request), when it has kept the signature of the timestamp from all the connected nodes, communicates to each connected node all the collected signatures.
					// This is a decentralized collective timestamp.
					SendTimestampSignatureToNode(timestampVector, node);
			}).Start();
		}

		/// <summary>
		/// The node at zero level (the entry point of the request), when it has kept the signature of the timestamp from all the connected nodes, communicates to each connected node all the collected signatures.
		/// This is a decentralized collective timestamp.
		/// The connected node has previously received an "element", which is held in stand-by until it receives the certified timestamp.
		/// This operation will unlock the stand-by if everything is regular
		/// </summary>
		/// <param name="timestampVector">The timestamp signed by all connected nodes</param>
		/// <param name="toNode">The connected node to send this data to</param>
		private void SendTimestampSignatureToNode(ObjToNode.TimestampVector timestampVector, Node toNode)
		{
			new Thread(() =>
			{
				//Verify if the node is disconnected
				if (!_networkConnection.NodeList.Contains(toNode)) return;
				var answer = Answer(SendRequest(toNode, StandardMessages.SendTimestampSignatureToNode, timestampVector));
			}).Start();
		}

		internal enum StandardMessages
		{
			NetworkNodes,
			ImOnline,
			ImOffline,
			RequestTestSpeed,
			TestSpeed,
			AddToPipeline,
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
			[XmlElement]
			public string Signature;
			public int Speed;
			[XmlElement]
			public long Timestamp;
			private byte[] HashData(uint ipNodeToTesting)
			{
				var data = Converter.GetBytes(ipNodeToTesting).Concat(Converter.GetBytes(Speed)).Concat(Converter.GetBytes(Timestamp)).ToArray();
				return Utility.GetHash(data);
			}

			public void SignTheResult(Node myNode, uint ipNodeToTesting, long currentTimestamp)
			{
				Timestamp = currentTimestamp;
				Signature = Convert.ToBase64String(myNode.Rsa.SignHash(HashData(ipNodeToTesting), CryptoConfig.MapNameToOID("SHA256")));
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