using System;
using System.Collections.Generic;
using System.Web;
using System.Xml.Serialization;
using static NetworkManager.Protocol;

namespace NetworkManager
{
	public class Device
	{
		protected Device(VirtualDevice virtualDevice)
		{
			OnlineDetection = new OnlineDetectionClass(this);
			if (virtualDevice != null)
			{
				_bd = BaseDevices.Find(x => x.VirtualDevice == virtualDevice) ?? new BaseDevice();
				virtualDevice.Device = _bd;
				VirtualDevice = virtualDevice;
			}
			else
			{
				if (_realDevice == null)
					_realDevice = new BaseDevice();
				_bd = _realDevice;
			}
			OnReceivesHttpRequest = _bd.OnReceivesHttpRequest;
			if (!BaseDevices.Contains(_bd))
				BaseDevices.Add(_bd);
		}
		internal static VirtualDevice FindDeviceByAddress(string address)
		{
			var uriAddress = new Uri(address);
			var domain = uriAddress.GetLeftPart(UriPartial.Authority);
			return VirtualDevices.Find(x => x.Address == domain);
		}
		/// <summary>
		/// Returns the list of virtual devices already initialized, and the real device if already initialized
		/// </summary>
		private static List<VirtualDevice> VirtualDevices
		{
			get
			{
				var list = new List<VirtualDevice>();
				foreach (var baseDevice in BaseDevices)
				{
					list.Add(baseDevice.VirtualDevice);
				}
				return list;
			}
		}
		private static readonly List<BaseDevice> BaseDevices = new List<BaseDevice>();
		private readonly BaseDevice _bd;
		internal List<NetworkConnection> Networks => _bd.Networks;
		public string MachineName => _bd.MachineName;

		/// <summary>
		/// If the internet works correctly it returns "true". If the auto detect realizes that there is no internet connection then it returns "false".
		/// In this case, auto detect will continue to monitor the network and this function will return "true" as soon as the connection returns.
		/// </summary>
		public bool IsOnline { get => _bd.IsOnline; internal set => _bd.IsOnline = value; }
		internal DateTime Now => BaseDevice.Now();
		internal VirtualDevice VirtualDevice { get => _bd.VirtualDevice; private set => _bd.VirtualDevice = value; }
		private static BaseDevice _realDevice;
		internal class BaseDevice
		{
			public readonly List<NetworkConnection> Networks = new List<NetworkConnection>();
			public string MachineName
			{
				get
				{
					var result = VirtualDevice?.MachineName ?? Environment.MachineName;
					return result;
				}
			}
			public bool IsOnline { get; internal set; } = true;

			//internal bool _isOnline = true;
			//public bool IsOnline { get => _isOnline;
			//  private set { _isOnline = value; }
			//}
			internal static DateTime Now()
			{
				return DateTime.UtcNow;
			}
			internal VirtualDevice VirtualDevice;
			/// <summary>
			/// This procedure receives an http request and processes the response based on the input received and the protocol
			/// </summary>
			/// <param name="queryString">QueryString Collection</param>
			/// <param name="form">Form Collection</param>
			/// <param name="fromIp">the IP of who generated the request</param>
			/// <param name="contentType">The ContentType of the answer</param>
			/// <param name="outputStream">The stream to which the reply will be sent</param>
			/// <returns>True if the operation was successful</returns>
			public bool OnReceivesHttpRequest(System.Collections.Specialized.NameValueCollection queryString, System.Collections.Specialized.NameValueCollection form, string fromIp, out string contentType, System.IO.Stream outputStream)
			{
				var networkName = queryString["network"];
				foreach (var network in Networks)
				{
					if (networkName != network.NetworkName) continue;
					var appName = queryString["app"];
					var toUser = queryString["toUser"];
					var fromUser = queryString["fromUser"];
					var post = queryString["post"];
					var request = queryString["request"];
					int.TryParse(queryString["secTimeout"], out var secTimeout);
					int.TryParse(queryString["secWaitAnswer"], out var secWaitAnswer);

					string xmlObject = null;
					if (form["object"] != null)
						xmlObject = HttpUtility.UrlDecode((form["object"]));
					if (toUser != MachineName && !toUser.StartsWith(MachineName + ".")) continue;
					var parts = toUser.Split('.'); // [0]=MachineName, [1]=PluginName
					string pluginName = null;
					if (parts.Length > 1)
					{
						pluginName = parts[1];
					}
					object returnObject = null;
#if !DEBUG
              try
              {
#endif
					if (!string.IsNullOrEmpty(pluginName))
					{
						//foreach (PluginManager.Plugin Plugin in AllPlugins())
						//{
						//  if (Plugin.IsEnabled(Setting))
						//  {
						//    if (Plugin.Name == PluginName)
						//    {
						//      Plugin.PushObject(Post, XmlObject, Form, ReturnObject);
						//      break;
						//    }
						//  }
						//}
					}
					else
					{
						if (!string.IsNullOrEmpty(post))//Post is a GetType Name
						{
							//Is a object transmission
							if (string.IsNullOrEmpty(fromUser))
								returnObject = "error: no server name setting";
							else if (network.Protocol.OnReceivingObjectsActions.ContainsKey(post))
								returnObject = network.Protocol.OnReceivingObjectsActions[post](xmlObject);
						}
						if (!string.IsNullOrEmpty(request))
						//Is a request o object
						{
							if (network.Protocol.OnRequestActions.ContainsKey(request))
								returnObject = network.Protocol.OnRequestActions[request](xmlObject);
							if (Enum.TryParse(request, out StandardMessages rq))
							{
								switch (rq)
								{
									case StandardMessages.NetworkNodes:
										returnObject = network.NodeList.ListWithComingSoon();
										break;
									case StandardMessages.GetStats:
										returnObject = new Stats { NetworkLatency = (int)network.PipelineManager.Stats24H.NetworkLatency.TotalMilliseconds };
										break;
									case StandardMessages.SendElementsToNode when Converter.XmlToObject(xmlObject, typeof(List<ObjToNode>), out var objElements):
										{
											var uintFromIp = Converter.IpToUint(fromIp);
											Node fromNode = null;
											for (var nTry = 0; nTry < 2; nTry++)
											{
												fromNode = network.NodeList.Find((x) => x.Ip == uintFromIp);
												if (fromNode != null)
													break;
												System.Threading.Thread.Sleep(1000);
											}
											if (fromNode == null)
											{
												returnObject = StandardAnswer.Unauthorized;
												System.Diagnostics.Debugger.Break();
											}
											else
												returnObject = network.PipelineManager.AddLocalFromNode((List<ObjToNode>)objElements, fromNode) ?? (object)StandardAnswer.Ok;
											break;
										}
									case StandardMessages.SendElementsToNode:
										returnObject = StandardAnswer.Error;
										break;
									case StandardMessages.SendTimestampSignatureToNode when Converter.XmlToObject(xmlObject, typeof(ObjToNode.TimestampVector), out var timestampVector):
										{
											//The node at level 1 receives the decentralized timestamp from node at level 0. At this point if everything is correct, the elements in stand by will be propagated on all the nodes.
											var uintFromIp = Converter.IpToUint(fromIp);
											var fromNode = network.NodeList.Find((x) => x.Ip == uintFromIp);
											if (fromNode != null)
												returnObject = network.PipelineManager.UnlockElementsInStandBy((ObjToNode.TimestampVector)timestampVector, uintFromIp) ? StandardAnswer.Ok : StandardAnswer.Error;
											break;
										}
									case StandardMessages.SendTimestampSignatureToNode:
										returnObject = StandardAnswer.Error;
										break;
									case StandardMessages.AddToPipeline:
										returnObject = network.PipelineManager.AddLocal(xmlObject) ? StandardAnswer.Ok : StandardAnswer.Error;
										break;
									case StandardMessages.ImOffline:
									case StandardMessages.ImOnline:
										{
											if (Converter.XmlToObject(xmlObject, typeof(Node), out var objNode))
											{
												var node = (Node)objNode;
												if (rq == StandardMessages.ImOnline)
													if (node.CheckIp() && node.Ip != Converter.IpToUint(fromIp))
														returnObject = StandardAnswer.IpError;
													else if (network.NodeList.Find(x => x.Ip == node.Ip) != null)
														returnObject = StandardAnswer.DuplicateIp;
													else if (network.Protocol.DecentralizedSpeedTest(node, out var speedTestResults))
													{
														//If the decentralized speed test is passed, then it propagates the notification on all the nodes that there is a new online node.
														network.Protocol.NotificationNewNodeIsOnline(node, speedTestResults);
														returnObject = StandardAnswer.Ok;
													}
													else
														returnObject = StandardAnswer.TooSlow;
											}
											else
												returnObject = StandardAnswer.Error;

											break;
										}
									case StandardMessages.RequestTestSpeed when Converter.XmlToObject(xmlObject, typeof(Node), out var objNode):
										{
											var node = (Node)objNode;
											returnObject = network.Protocol.SpeedTestSigned(node);
											break;
										}
									case StandardMessages.RequestTestSpeed:
										returnObject = -1;
										break;
									case StandardMessages.TestSpeed:
										returnObject = new string('x', 131072); // 1/8 of MB (1MB = 1048576)
										break;
									default:
										throw new ArgumentOutOfRangeException();
								}
								contentType = "text/xml;charset=utf-8";
								var xml = new XmlSerializer(returnObject.GetType());
								var xmlns = new XmlSerializerNamespaces();
								xmlns.Add(string.Empty, string.Empty);
								xml.Serialize(outputStream, returnObject, xmlns);
								return true;
							}
						}
					}
#if !DEBUG
              }
              catch (Exception ex)
              {
                System.Diagnostics.Debug.Print(ex.Message);
                System.Diagnostics.Debugger.Break();
                ReturnObject = ex.Message;
              }
#endif
					if (returnObject == null || !string.IsNullOrEmpty(request)) continue;
					{
						var vector = new Communication.ObjectVector(toUser, returnObject);
						contentType = "text/xml;charset=utf-8";
						var xml = new XmlSerializer(typeof(Communication.ObjectVector));
						var xmlns = new XmlSerializerNamespaces();
						xmlns.Add(string.Empty, string.Empty);
						xml.Serialize(outputStream, vector, xmlns);
						return true;
					}
					//if (Post != "")
					//  var se = new SpolerElement(AppName, FromUser, ToUser, QueryString("post"), XmlObject, SecTimeout);

					//if (string.IsNullOrEmpty(Request))
					//  SendObject(AppName, FromUser, ToUser, SecWaitAnswer);
					//else
					//  switch (Request)
					//  {
					//    default:
					//      break;
					//  }
				}
				contentType = null;
				return false;
			}
		}
		internal readonly OnlineDetectionClass OnlineDetection;
		internal class OnlineDetectionClass
		{
			public OnlineDetectionClass(Device device)
			{
				_checkInternetConnection = new System.Timers.Timer(30000) { AutoReset = true, Enabled = false, };
				_checkInternetConnection.Elapsed += (sender, e) => ElapsedCheckInternetConnection();
				_device = device;
			}
			private readonly Device _device;
			private bool CheckImOnline()
			{
				if (_device._bd.VirtualDevice != null)
					return _device._bd.VirtualDevice.IsOnline;
				try
				{
					var r1 = new System.Net.NetworkInformation.Ping().Send("www.google.com.mx").Status == System.Net.NetworkInformation.IPStatus.Success;
					var r2 = new System.Net.NetworkInformation.Ping().Send("www.bing.com").Status == System.Net.NetworkInformation.IPStatus.Success;
					return r1 && r2;
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.Print(ex.Message);
					System.Diagnostics.Debugger.Break();
				}
				return false;
			}
			private readonly System.Timers.Timer _checkInternetConnection;
			private void ElapsedCheckInternetConnection()
			{
				if (_runningCheckInternetConnection != 1) return;
				_device._bd.IsOnline = CheckImOnline();
				if (!_device._bd.IsOnline) return;
				_checkInternetConnection.Stop();
				_runningCheckInternetConnection = 0;
				_device.Networks.ForEach(x => x.Start());
			}
			private int _runningCheckInternetConnection = 0;
			/// <summary>
			/// He waits and checks the internet connection, and starts the communication protocol by notifying the online presence
			/// </summary>
			internal void WaitForInternetConnection()
			{
				_runningCheckInternetConnection += 1;
				if (_runningCheckInternetConnection != 1) return;
				_checkInternetConnection.Start();
				new System.Threading.Thread(ElapsedCheckInternetConnection).Start();
			}
		}
		public delegate bool OnReceivesHttpRequestDelegate(System.Collections.Specialized.NameValueCollection queryString, System.Collections.Specialized.NameValueCollection form, string fromIp, out string contentType, System.IO.Stream outputStream);
		/// <summary>
		/// This procedure receives an http request and processes the response based on the input received and the protocol
		/// </summary> 
		/// <param name="queryString">QueryString Collection</param>
		/// <param name="form">Form Collection</param>
		/// <param name="fromIP">the IP of who generated the request</param>
		/// <param name="contentType">The ContentType of the answer</param>
		/// <param name="outputStream">The stream to which the reply will be sent</param>
		/// <returns>True if the operation was successful</returns>
		public readonly OnReceivesHttpRequestDelegate OnReceivesHttpRequest;
	}

}
