using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Timer = System.Timers.Timer;

namespace NetworkManager
{
	public class NodeList : List<Node>
	{
		public NodeList(NetworkConnection networkConnection)
		{
			_networkConnection = networkConnection;
		}
		private readonly NetworkConnection _networkConnection;
		private readonly List<Node> ComingSoonNodes = new List<Node>();
		public readonly List<Node> NewNodes = new List<Node>();
		public readonly List<Node> RecentOfflineNodes = new List<Node>();
		public new void Add(Node node)
		{
			lock (this)
			{
				if (node.Timestamp != null)
					Add(node, node.Timestamp);
				else
					AddToBase(node);
				Changed();
			}
		}
		public new void AddRange(IEnumerable<Node> nodes)
		{
			lock (this)
			{
				foreach (var node in nodes)
					if (node.Offline != null)
						AddToRecentOfflineNodesList(node);
					else if (node.Timestamp != null)
						Add(node, node.Timestamp);
					else
						AddToBase(node);
				Changed();
			}
		}
		/// <summary>
		///   Add a new node to the networkConnection. Using this function, the new node will be added to all nodes simultaneously.
		/// </summary>
		/// <param name="node">Node to add</param>
		/// <param name="timestamp">Timestamp of the notification</param>
		internal void Add(Node node, long? timestamp)
		{
			//the node will be online starting from timestamp + DeltaTimeMs;
			node.Timestamp = timestamp;
			lock (ComingSoonNodes) ComingSoonNodes.Add(node);
			// Add this node after these time from timestamp
			var enabledFrom = timestamp + TimeSpan.FromMilliseconds(DoubleDeltaTimeMs).Ticks;
			var remainingTime = enabledFrom - _networkConnection.Now.Ticks;
			var ms = (int)TimeSpan.FromTicks((long)remainingTime).TotalMilliseconds;
			var timer = new Timer(ms >= 1 ? ms : 1) { AutoReset = false };
			timer.Elapsed += (sender, e) =>
			{
				lock (ComingSoonNodes)
				{
					if (!ComingSoonNodes.Contains(node)) return;
					node.Timestamp = null;
					Add(node);
					ComingSoonNodes.Remove(node);
				}
			};
			timer.Start();
		}

		private void AddToBase(Node node)
		{
			base.Add(node);
			lock (NewNodes)
				NewNodes.Add((node));
			var timer = new Timer(DeltaTimeMs) { AutoReset = false };
			timer.Elapsed += (sender, e) =>
			{
				lock (NewNodes) NewNodes.Remove(node);
			};
			timer.Start();
		}

		
		/// <summary>
		/// Update the list of online nodes by querying the networkConnection.
		/// </summary>
		public void Update()
		{
			Wait.Reset(); // Suspends the reading of the node lists
			if (_networkConnection.MyNode == null)
				_update();
			else
			{
				//The list of nodes is updated when this node is also online, so the operation is postponed.
				var timer = new Timer(DoubleDeltaTimeMs - TimeNeededForTheUpdateMs) { AutoReset = false };
				timer.Elapsed += (sender, e) => _update();
				timer.Start();
			}
		}
		internal const int TimeNeededForTheUpdateMs = 3000; //This is the maximum time to perform the Update() operation
		internal bool RecentlyUpdated;

		/// <summary>
		/// Update the list of online nodes by querying the networkConnection
		/// </summary>
		internal void _update()
		{
			// note: lock (this){...} is not necessary, is present inside AddRange() and Add()
			Wait.Reset(); // Suspends the reading of the node lists
			RecentlyUpdated = true;
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
			Wait.Set(); // Re-enable access to reading node lists
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
					node.Offline = _networkConnection.Now.AddMilliseconds(DeltaTimeMs).Ticks;
					AddToRecentOfflineNodesList(node);
					base.Remove(node);
					Changed(true);
					return true;
				}
				else
					return false;
			}
		}

		private void AddToRecentOfflineNodesList(Node node)
		{
			var removeAt = new DateTime((long)node.Offline);
			var removeAfterMs = (removeAt - _networkConnection.Now).TotalMilliseconds;
			if (removeAfterMs > 0)
			{
				lock (RecentOfflineNodes)
					RecentOfflineNodes.Add(node);
				var timer = new Timer(removeAfterMs) { AutoReset = false };
				timer.Elapsed += (sender, e) =>
				{
					lock (RecentOfflineNodes) RecentOfflineNodes.Remove(node);
				};
				timer.Start();
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

		private EventWaitHandle Wait = new AutoResetEvent(false);
		public void WaitForUpdate()
		{
			//Wait.WaitOne();
		}

		/// <summary>
		/// Returns the list of currently active nodes, plus those that are imminent online.
		/// The nodes that will be imminent online, have set the timestamp, so that who receives this list knows when to activate the communication with these nodes.
		/// </summary>
		/// <returns></returns>
		public List<Node> ListWithComingSoon()
		{
			WaitForUpdate();
			lock (this)
				lock (ComingSoonNodes)
					return this.Concat(ComingSoonNodes).ToList();
		}
		public List<Node> ListWithRecentNodes()
		{
			WaitForUpdate();
			lock (this)
				lock (RecentOfflineNodes)
					return this.Concat(RecentOfflineNodes).ToList();
		}
		public List<Node> ListWithRecentAndComingSoon()
		{
			WaitForUpdate();
			lock (this)
				lock (ComingSoonNodes)
					lock (RecentOfflineNodes)
						return this.Concat(RecentOfflineNodes).Concat(ComingSoonNodes).ToList();
		}
		public List<Node> ComingSoonAndRecentOfflineNodes()
		{
			WaitForUpdate();
			lock (ComingSoonNodes)
				lock (RecentOfflineNodes)
					return ComingSoonNodes.Concat(RecentOfflineNodes).ToList();
		}

		public List<Node> ComingSoonAndNewAndRecentOfflineNodes()
		{
			WaitForUpdate();
			lock (ComingSoonNodes)
				lock (NewNodes)
					lock (RecentOfflineNodes)
						return ComingSoonNodes.Concat(NewNodes).Concat(RecentOfflineNodes).ToList();
		}

	}
}