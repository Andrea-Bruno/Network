using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;

namespace NetworkManager
{
	public class Element
	{
		public long Timestamp;
		private string _xmlObject;
		public string XmlObject { get => _xmlObject; set => _xmlObject = Utility.MinifyXml(value); }
	}
	public class ObjToNode
	{
		public Element GetElement { get; }
		public ObjToNode() { GetElement = new Element(); }
		public ObjToNode(Element element) { GetElement = element; }
		public int Level;
		[DataMember]
		public string XmlObject { get => GetElement.XmlObject; set => GetElement.XmlObject = value; }
		[DataMember]
		public long Timestamp { get => GetElement.Timestamp; set => GetElement.Timestamp = value; }
		public string TimestampSignature;
		/// <summary>
		/// Class used exclusively to transmit the timestamp certificates to the node who sent the object to be signed
		/// </summary>
		public class TimestampVector
		{
			public void Add(int hash, string sign)
			{
				Array.Resize(ref Signatures, Signatures.Length + 1);
				Signatures[Signatures.Length - 1] = new Signature() { Hash = hash, Sign = sign };
			}
			public bool TryGetValue(int hash, out string sign)
			{
				var signature = Array.Find(Signatures, x => x.Hash == hash);
				sign = signature?.Sign;
				return signature != null;
			}
			/// <summary>
			/// "int" is the short hash of ObjToNode and "string" is the SignedTimestamp for the single ObjToNode
			/// </summary>
			[XmlArray] public Signature[] Signatures = new Signature[0];
			public class Signature
			{
				public int Hash; // Is the short hash of ObjToNode
				public string Sign; // Is the SignedTimestamp for the single ObjToNode
			}
		}
		internal int ShortHash()
		{
			Debug.WriteLine(Timestamp.GetHashCode() ^ XmlObject.GetHashCode());
			return Timestamp.GetHashCode() ^ XmlObject.GetHashCode();
		}

		private byte[] Hash()
		{
			var data = Converter.GetBytes(Timestamp).Concat(Converter.StringToByteArray(XmlObject)).ToArray();
			var hashBytes = Utility.GetHash(data);
			return hashBytes;
		}

		/// <summary>
		/// Check the node that sent this object, have assigned a correct timestamp, if so it generates its own signature.
		/// </summary>
		/// <param name="myNode">Your own Node</param>
		/// <param name="now">Current date and time</param>
		/// <returns>Returns the signature if the timestamp assigned by the node is correct, otherwise null</returns>
		internal string CreateTheSignatureForTheTimestamp(Node myNode, DateTime now)
		{
			var remoteNow = new DateTime(Timestamp);
			const double margin = 0.5; // ***Calculates a margin because the clocks on the nodes may not be perfectly synchronized
			if (now < remoteNow.AddSeconds(-margin))
			{
				Utility.Log("signature", "signature rejected for incongruous timestamp");
				System.Diagnostics.Debugger.Break();
				return null;
			}
			const int maximumTimeToTransmitTheDataOnTheNode = 2; // ***In seconds
			return now <= remoteNow.AddSeconds(maximumTimeToTransmitTheDataOnTheNode + margin) ? GetTimestampSignature(myNode) : null;
		}
		private string GetTimestampSignature(Node node)
		{
			var ipNode = Converter.GetBytes(node.Ip);
			var signedTimestamp = node.Rsa.SignHash(Hash(), System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"));
			return Convert.ToBase64String(ipNode.Concat(signedTimestamp).ToArray());
		}
		internal void AddFirstTimestamp(Node nodeLevel0, long timestamp)
		{
			Timestamp = timestamp;
			TimestampSignature = GetTimestampSignature(nodeLevel0);
			Debug.WriteLine(timestamp);
			Debug.WriteLine(XmlObject);
		}
		internal enum CheckSignedTimestampResult { Ok, NodeThatPutTheSignatureNotFound, InvalidSignature, NonCompliantNetworkConfiguration, SignaturesNotFromCorrectNode }

		/// <summary>
		/// Check if all the nodes responsible for distributing this data have put their signature on the timestamp
		/// </summary>
		/// <param name="networkConnection"></param>
		/// <param name="fromIp"></param>
		/// <returns>Result of the operation</returns>
		internal CheckSignedTimestampResult CheckSignedTimestamp(NetworkConnection networkConnection, uint fromIp)
		{
			var currentNodeList = networkConnection.NodeList.CurrentAndRecentNodes();
			const int lenT = 132;
			var signedTimestamps = Convert.FromBase64String(TimestampSignature);
			var ts = new List<byte[]>();
			Node firstNode = null;
			var nodes = new List<Node>();
			do
			{
				var timestamp = signedTimestamps.Take(lenT).ToArray();
				ts.Add(timestamp);
				signedTimestamps = signedTimestamps.Skip(lenT).ToArray();
			} while (signedTimestamps.Count() != 0);
			var hashBytes = Hash();
			foreach (var signedTs in ts)
			{
				ReadSignedTimespan(signedTs, out var ipNode, out var signature);
				var node = currentNodeList.Find((x) => x.Ip == ipNode);
				if (node == null)
					return CheckSignedTimestampResult.NodeThatPutTheSignatureNotFound;
				if (firstNode == null)
				{
					if (ipNode != fromIp)
						return CheckSignedTimestampResult.SignaturesNotFromCorrectNode;
					firstNode = node;
				}
				else
					nodes.Add(node);
				if (!node.Rsa.VerifyHash(hashBytes, System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"), signature))
					return CheckSignedTimestampResult.InvalidSignature;
			}
			var validateConnection = networkConnection.ValidateConnectionAtLevel0(firstNode, nodes);
			if (validateConnection==false) Debugger.Break();
			return validateConnection ? CheckSignedTimestampResult.Ok : CheckSignedTimestampResult.NonCompliantNetworkConfiguration;
		}
		internal bool CheckNodeThatStartedDistributingTheObject(Node fromNode)
		{
			ReadSignedTimespan(Convert.FromBase64String(TimestampSignature), out var ipNode, out var signature);
			return fromNode.Ip == ipNode && fromNode.Rsa.VerifyHash(Hash(), System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"), signature);
		}
		private static void ReadSignedTimespan(byte[] signedTimestamp, out uint ipNode, out byte[] signature)
		{
			ipNode = Converter.BytesToUint(signedTimestamp.Take(4).ToArray());
			signature = signedTimestamp.Skip(4).ToArray();
		}
	}
}
