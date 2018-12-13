using System;
using System.Collections.Generic;
using System.Linq;
using static NetworkManager.Protocol;

namespace NetworkManager
{
	public class NetworkConnection : Device
	{
		public delegate bool Execute(Node node);

		public delegate void SyncData(string xmlObject, long timestamp);

		/// <summary>
		///   The pipeline is a mechanism that allows you to distribute objects on the network by assigning a timestamp.
		///   The objects inserted in the pipeline will exit from it on all the nodes following the chronological order of input(they
		///   come out sorted by timestamp).
		///   The data output from the pipeline must be managed by actions that are programmed when the network is initialized.
		/// </summary>
		internal readonly PipelineManager PipelineManager;

		/// <summary>
		///   It creates communication mechanisms that can be used by a protocol to communicate between nodes via the Internet
		/// </summary>
		public readonly Communication Communication;

		public readonly Protocol Protocol;

		//internal InfoNode ThisNode;
		public readonly InfoNode ThisNode;


		//private string _MyAddress;
		//public string MyAddress { get { return _MyAddress; } }
		//public uint? MyIP { get { return MyNode?.IP; } }


		/// <summary>
		///   Contains the logic that establishes a mapping of the network and its subdivision to increase its performance.
		///   The network is divided at a logical level into many ring groups, with a recursive pyramidal structure
		/// </summary>
		internal readonly MappingNetwork MappingNetwork;

		internal readonly Node MyNode;
		internal readonly NodeList NodeList;
		[Obsolete("We recommend using this method from the Device class because each device could handle multiple networks", false)]
		public readonly new OnReceivesHttpRequestDelegate OnReceivesHttpRequest;
		/// <summary>
		///   This method initializes the network.
		///   You can join the network as a node, and contribute to decentralization, or hook yourself to the network as an
		///   external user.
		///   To create a node, set the MyAddress parameter with your web address.If MyAddress is not set then you are an external
		///   user.
		/// </summary>
		/// <param name="entryPoints">The list of permanent access points nodes, to access the network</param>
		/// <param name="networkName">The name of the infrastructure. For tests we recommend using "testnet"</param>
		/// <param name="myNode">Data related to your node. If you do not want to create the node, omit this parameter</param>
		public NetworkConnection(IEnumerable<Node> entryPoints, string networkName = "testnet", NodeInitializer myNode = null) : base(myNode?.VirtualDevice)
		{
			//if (VirtualDevice != null)
			//{
			//  //base = new Device() { VirtualDevice = VirtualDevice };
			//}
			Networks.Add(this);
			Communication = new Communication(this);
			Protocol = new Protocol(this);
			PipelineManager = new PipelineManager(this);
			MappingNetwork = new MappingNetwork(this);
			var entry = new List<Node>(entryPoints as Node[] ?? entryPoints.ToArray());
			NodeList = new NodeList(this);
			if (myNode != null)
			{
				MyNode = new Node(myNode);
				MyNode.Ip = VirtualDevice?.Ip ?? MyNode.DetectIp();
				var count = entry.Count;
				entry.RemoveAll(x => x.Address == MyNode.Address || x.Ip == MyNode.Ip);
				_imEntryPoint = count != entry.Count;
				ThisNode = new InfoNode(MyNode);
				NodeList.Add(MyNode);
			}
			entry.ForEach(x => x.DetectIp());
			NodeList.AddRange(entry);
			NetworkName = networkName;

			//Setup.NetworkConnection.MyAddress = MyAddress;
			//Setup.NetworkConnection.NetworkName = NetworkName;
			//if (EntryPoints != null)
			//  Setup.NetworkConnection.EntryPoints = EntryPoints;
#pragma warning disable CS0618 // 'NetworkConnection.OnReceivesHttpRequest' è obsoleto: 'We recommend using this method from the Device class because each device could handle multiple networks'
			OnReceivesHttpRequest = base.OnReceivesHttpRequest; //Is ok! Don't worry
#pragma warning restore CS0618 // 'NetworkConnection.OnReceivesHttpRequest' è obsoleto: 'We recommend using this method from the Device class because each device could handle multiple networks'
			OnlineDetection.WaitForInternetConnection();
		}
		private readonly bool _imEntryPoint;
		public string NetworkName { get; }
		public string MasterServerMachineName { get; }

		internal void Start()
		{
			NodeList.Update();
			if (MyNode == null) return;
			ThisNode.ConnectionStatus = Protocol.ImOnline(GetRandomNode());
			// if Answer = NoAnswer then I'm the first online node in the network  
			if (ThisNode.ConnectionStatus == StandardAnswer.NoAnswer && _imEntryPoint)
				ThisNode.ConnectionStatus = StandardAnswer.Ok; //I'm the first online node
			else
			{
				var networkLatency = 0;
				var stats1 = Protocol.GetStats(GetRandomNode());
				var stats2 = Protocol.GetStats(GetRandomNode());
				networkLatency = Math.Max(stats1?.NetworkLatency ?? 0, stats2?.NetworkLatency ?? 0);//***
				//MappingNetwork.SetNetworkSyncTimeSpan(networkLatency);
			}
			MappingNetwork.SetNodeNetwork();
		}
		public void GoOffline()
		{
			Protocol.ImOffline(GetRandomNode());
		}
#if DEBUG
		//==== REMOVE THE TEST IN THE FINAL VERSION
		public void Test()
		{
			const int l = 2;
			const int h = 1;
			var list = new List<Node>();
			for (var y = 0; y < h; y++)
				for (var x = 0; x < l; x++)
				{
					var node = new Node { Address = x + "," + y };
					list.Add(node);
				}
			NodeList.Clear();
			NodeList.AddRange(list);
			var myNode = GetRandomNode();
			MappingNetwork.SetNodeNetwork();
			MappingNetwork.GetXY(myNode, NodeList, out var x2, out var y2);
			var thisNode = MappingNetwork.GetNodeAtPosition(NodeList, x2, y2);
			var ok = MyNode == thisNode;
			var connections = MappingNetwork.GetConnections(1);
		}
#endif

		internal bool ValidateConnectionAtLevel0(Node nodeAtLevel0, List<Node> connections)
		{
			lock (NodeList)
				lock (NodeList.RecentOfflineNodes)
				{
					List<Node> possibleConnections;
					for (var n = 0; n <= NodeList.RecentOfflineNodes.Count; n++)
						if (n == 0)
						{
							possibleConnections = MappingNetwork.GetConnections(1, nodeAtLevel0, NodeList);
							if (possibleConnections.Count == connections.Count && connections.TrueForAll(x => possibleConnections.Contains(x)))
								return true;
						}
						else
						{
							var groupsNodesToAdd = (List<List<Node>>)Utility.GetPermutations(NodeList.RecentOfflineNodes, n);
							foreach (var nodesToAdd in groupsNodesToAdd)
							{
								var list = NodeList.Concat(nodesToAdd).ToList();
								list = list.OrderBy(x => x.Ip).ToList();
								possibleConnections = MappingNetwork.GetConnections(1, nodeAtLevel0, list);
								if (possibleConnections.Count == connections.Count && connections.TrueForAll(x => possibleConnections.Contains(x)))
									return true;
							}
						}
				}
			return false;
		}

		internal Node GetRandomNode()
		{
			lock (NodeList)
			{
				var min = 1;
				if (MyNode != null)
					min = 2;
				if (NodeList.Count < min) return null;
				Node randomNode;
				do
				{
					randomNode = NodeList[new Random().Next(NodeList.Count)];
				} while (randomNode == MyNode);
				return randomNode;
			}
		}

		/// <summary>
		///   Performs a specific code addressed to a randomly selected node.
		/// </summary>
		/// <param name="execute">The instructions to be executed</param>
		public bool InteractWithRandomNode(Execute execute)
		{
			bool ok;
			var tryNode = 0;
			do
			{
				tryNode++;
				var node = GetRandomNode();
				var count = 0;
				do
				{
					count++;
					ok = execute.Invoke(node);
				} while (ok == false && count < 2);
			} while (ok == false && tryNode < 3);

			return ok;
		}

		/// <summary>
		///   Send an object to the network to be inserted in the shared pipeline
		/// </summary>
		/// <param name="Object">Object to send</param>
		/// <returns></returns>
		public StandardAnswer AddToSharedPipeline(object Object) => Protocol.AddToSharedPipeline(GetRandomNode(), Object);

		/// <summary>
		///   Add a action used to local sync the objects coming from the pipeline
		/// </summary>
		/// <param name="action">Action to execute for every object</param>
		/// <param name="forObjectName">Indicates what kind of objects will be treated by this action</param>
		public bool AddSyncDataFromPipelineAction(SyncData action, string forObjectName) => PipelineManager.AddSyncDataAction(action, forObjectName);

		internal void PipelineElementsChanged(int NElements)
		{
			OnPipelineElementsChanged?.Invoke(EventArgs.Empty, NElements);			
		}
		public event EventHandler<int> OnPipelineElementsChanged;
	}
}