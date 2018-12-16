using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;

namespace NetworkManager
{
	public class NodeList : List<Node>
	{
		public NodeList(NetworkConnection networkConnection)
		{
			_networkConnection = networkConnection;
		}
		private readonly NetworkConnection _networkConnection;
		public new void Add(Node node)
		{
			lock (this)
			{
				if (node.Timestamp != 0)
					Add(node, node.Timestamp);
				else
					base.Add(node);
				Changed();
			}
		}
		public new void AddRange(IEnumerable<Node> nodes)
		{
			lock (this)
			{
				foreach (var node in nodes)
					if (node.Timestamp != 0)
						Add(node, node.Timestamp);
					else
						base.Add(node);
				Changed();
			}
		}
		/// <summary>
		/// Update the list of online nodes by querying the networkConnection.
		/// </summary>
		public void Update()
		{
			if (_networkConnection.MyNode == null)
				_update();
			else
			{
				//The list of nodes is updated when this node is also online, so the operation is postponed.
				var timer = new Timer(DoubleDeltaTimeMs) { AutoReset = false };
				timer.Elapsed += (sender, e) => _update();
				timer.Start();
			}
		}
		/// <summary>
		/// Update the list of online nodes by querying the networkConnection
		/// </summary>
		private void _update()
		{
			var connectionNode = _networkConnection.GetRandomNode();
			var nodes = _networkConnection.Protocol.GetNetworkNodes(connectionNode);
			if (_networkConnection.MyNode == null)
			{
				AddRange(nodes);
				return;
			}
			nodes.RemoveAll(x => x.Address == _networkConnection.MyNode.Address || x.Ip == _networkConnection.MyNode.Ip);
			Clear();
			AddRange(nodes);
			Add(_networkConnection.MyNode);
		}

		public new void Clear()
		{
			lock (this)
			{
				base.Clear();
				Changed();
			}
		}

		public new bool Remove(Node node)
		{
			return Remove(node.Ip);
		}

		public void Remove(Node node, DateTime time)
		{
			var interval = (time - _networkConnection.Now).TotalMilliseconds;
			var timer = new Timer(interval > 0 ? interval : 1) { AutoReset = false };
			timer.Elapsed += (sender, e) => { Remove(node.Ip); };
			timer.Start();
		}

		private bool Remove(uint nodeIp)
		{
			lock (this)
			{
				lock (ComingSoonNodes)
					ComingSoonNodes.Remove(ComingSoonNodes.Find(x => x.Ip == nodeIp));
				var node = Find(x => x.Ip == nodeIp);
				if (node != null)
				{
					lock (RecentOfflineNodes)
						RecentOfflineNodes.Add((node));
					var timer = new Timer(DeltaTimeMs) { AutoReset = false };
					timer.Elapsed += (sender, e) =>
					{
						lock (RecentOfflineNodes) RecentOfflineNodes.Remove(node);
					};
					timer.Start();
					base.Remove(node);
					Changed(true);
					return true;
				}
				else
					return false;
			}
		}

		private void Changed(bool removed = false)
		{
			if (!removed) Sort((x, y) => x.Ip.CompareTo(y.Ip));
			//NetworkConnection.NodeList = this.OrderBy(o => o.Ip).ToList();
			_networkConnection.MappingNetwork.SetNodeNetwork();
			_networkConnection.ThisNode.RaiseEventOnNodeListChanged(Count);
		}

		/// <summary>
		/// This is the theoretical time limit that a data item takes to cover the shared pipeline
		/// </summary>
		internal int DoubleDeltaTimeMs => (int)MappingNetwork.NetworkSyncTimeSpan(this.Count).TotalMilliseconds * 2 + marginMs; //Doubling the time you are sure that with the update you can also read any nodes that are about to become online
		internal int DeltaTimeMs => (int)MappingNetwork.NetworkSyncTimeSpan(this.Count).TotalMilliseconds + marginMs;
		private const int marginMs = 1000; // *** We add a small moment of latency since the communication is expelled from the shared pipeline and the node is actually offilne

		/// <summary>
		///   Add a new node to the networkConnection. Using this function, the new node will be added to all nodes simultaneously.
		/// </summary>
		/// <param name="node">Node to add</param>
		/// <param name="timestamp">Timestamp of the notification</param>
		internal void Add(Node node, long timestamp)
		{
			//the node will be online starting from timestamp + DeltaTimeMs;
			node.Timestamp = timestamp;
			lock (ComingSoonNodes) ComingSoonNodes.Add(node);
			// Add this node after these time from timestamp
			var TimeNeededForTheUpdate = 7000; //This is the maximum time to perform the Update() operation
			var enabledFrom = timestamp + TimeSpan.FromMilliseconds(DoubleDeltaTimeMs + TimeNeededForTheUpdate).Ticks;
			var remainingTime = enabledFrom - _networkConnection.Now.Ticks;
			var ms = (int)TimeSpan.FromTicks(remainingTime).TotalMilliseconds;
			var timer = new Timer(ms >= 1 ? ms : 1) { AutoReset = false };
			timer.Elapsed += (sender, e) =>
			{
				lock (ComingSoonNodes)
				{
					if (!ComingSoonNodes.Contains(node)) return;
					node.Timestamp = 0;
					Add(node);
					ComingSoonNodes.Remove(node);
				}
			};
			timer.Start();
		}
		/// <summary>
		/// Returns the list of currently active nodes, plus those that are imminent online.
		/// The nodes that will be imminent online, have set the timestamp, so that who receives this list knows when to activate the communication with these nodes.
		/// </summary>
		/// <returns></returns>
		public List<Node> ListWithComingSoon()
		{
			lock (this)
				lock (ComingSoonNodes)
					return this.Concat(ComingSoonNodes).ToList();
		}
		public List<Node> ListWithRecentAndComingSoon()
		{
			lock (this)
				lock (ComingSoonNodes)
					lock (RecentOfflineNodes)
						return this.Concat(RecentOfflineNodes).Concat(ComingSoonNodes).ToList();
		}

		private readonly List<Node> ComingSoonNodes = new List<Node>();
		public readonly List<Node> RecentOfflineNodes = new List<Node>();
		public List<Node> CurrentAndRecentNodes()
		{
			lock (this)
				lock (RecentOfflineNodes)
					return this.Concat(RecentOfflineNodes).ToList();
		}
		public List<Node> ComingSoonAndRecentNodes()
		{
			lock (ComingSoonNodes)
				lock (RecentOfflineNodes)
					return ComingSoonNodes.Concat(RecentOfflineNodes).ToList();
		}

	}
}