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
		/// <summary>
		/// Do not worry: it's a joint signature, its data includes the node that put the signature and the calculation is done on the hash of the timestamp + xml of the element
		/// </summary>
		public string TimestampSignature;
		private string _xmlObject;
		public string XmlObject { get => _xmlObject; set => _xmlObject = Utility.MinifyXml(value); }
	}
	public class ObjToNode
	{
		public Element GetElement { get; }
		public ObjToNode() { GetElement = new Element(); }
		public ObjToNode(Element element, int toLevel)
		{
			GetElement = element;
			Level = toLevel;
		}
		public int Level;
		[DataMember]
		public string XmlObject { get => GetElement.XmlObject; set => GetElement.XmlObject = value; }
		[DataMember]
		public long Timestamp { get => GetElement.Timestamp; set => GetElement.Timestamp = value; }
		/// <summary>
		/// Do not worry!: it's a joint signature, its data includes the node that put the signature and the calculation is done on the hash of the timestamp + xml of the element
		/// </summary>
		public string TimestampSignature { get => GetElement.TimestampSignature; set => GetElement.TimestampSignature = value; }
		internal CheckSignedTimestampResult AddTimestampSignature(string TimestampSignature, Node node)
		{
			var signature = new SignatureOfTimestamp(Convert.FromBase64String(TimestampSignature), out var check);
			if (check == CheckSignedTimestampResult.Ok)
				check = CheckSignature(signature, node.Ip, true, node);
			if (check == CheckSignedTimestampResult.Ok)
				this.TimestampSignature += TimestampSignature;
			else
			{
				this.TimestampSignature += Convert.ToBase64String(Converter.GetBytes(node.Ip).Concat(SignatureOmitted).ToArray()); //Add empty signature (omitted signature)
				FlagSignatureError = check;
			}
			return check;
		}
		internal CheckSignedTimestampResult FlagSignatureError;
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
			return Timestamp.GetHashCode() ^ XmlObject.GetHashCode();
		}

		internal byte[] Hash()
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
				// System.Diagnostics.Debugger.Break();
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
			
#if DEBUG
			Debug.WriteLine(timestamp);
			Debug.WriteLine(System.Threading.Thread.CurrentThread.ManagedThreadId);
			Debug.WriteLine(TimestampSignature);
			if (TimestampSignature == null)
				Debugger.Break();
#endif
		}
		internal enum CheckSignedTimestampResult { Ok, NodeThatPutTheSignatureNotFound, InvalidSignature, SigatureError, SignatureOmitted, NonCompliantNetworkConfiguration, SignaturesNotFromCorrectNode, WrongSignatureSize }

		/// <summary>
		/// Check if all the nodes responsible for distributing this data have put their signature on the timestamp
		/// </summary>
		/// <param name="networkConnection"></param>
		/// <param name="fromIp"></param>
		/// <returns>Result of the operation</returns>
		internal CheckSignedTimestampResult CheckSignedTimestamp(NetworkConnection networkConnection, uint fromIp, bool fromLevel0 = false)
		{
			if (TimestampSignature == null)
				return CheckSignedTimestampResult.SignatureOmitted;
			var signedTimestamps = Convert.FromBase64String(TimestampSignature);
			var hashBytes = Hash();
			List<SignatureOfTimestamp> signatures;
			try
			{
				signatures = ReadSignedTimespan(signedTimestamps);
			}
			catch
			{
				Utility.Log("signature", "signature error from IP " + Converter.UintToIp(fromIp));
				Debugger.Break();
				return CheckSignedTimestampResult.SigatureError;
			}

			var nodes = new List<Node>();
			var check = CheckSignedTimestampResult.Ok;
			for (int tryIndex = 1; tryIndex <= 2; tryIndex++)
			{
				var currentNodeList = networkConnection.NodeList.ListWithRecentAndComingSoon();
				foreach (var signatureOfTimestamp in signatures)
				{
					check = CheckSignature(signatureOfTimestamp, fromIp, nodes.Count == 0 && fromLevel0, currentNodeList, out var node, hashBytes);
					if (check != CheckSignedTimestampResult.Ok) break;
					nodes.Add(node);
				}
				if (networkConnection.NodeList.RecentlyUpdated && check == CheckSignedTimestampResult.NodeThatPutTheSignatureNotFound)
				{
					// It is statistically unlikely but it could happen that a node is added to the network, in the short span of time that passes between updating the list of nodes and the online entry of this node.
					nodes.Clear();
					networkConnection.NodeList.RecentlyUpdated = false;
					networkConnection.NodeList._update();
				}
				else
					break;
			}
			if (check != CheckSignedTimestampResult.Ok) return check;
			var validateConnection = networkConnection.ValidateConnectionAtLevel0(nodes.First(), nodes.Skip(1).ToList());
			if (validateConnection == false) Debugger.Break();
			return validateConnection ? CheckSignedTimestampResult.Ok : CheckSignedTimestampResult.NonCompliantNetworkConfiguration;
		}

		internal CheckSignedTimestampResult CheckSignature(SignatureOfTimestamp signatureOfTimestamp, uint fromIp, bool checkFromIp, List<Node> currentNodeList, out Node node, byte[] hashBytes = null)
		{
			node = currentNodeList.Find((x) => x.Ip == signatureOfTimestamp.IpNode);
			if (node == null)
				return CheckSignedTimestampResult.NodeThatPutTheSignatureNotFound;
			return CheckSignature(signatureOfTimestamp, fromIp, checkFromIp, node, hashBytes);
		}
		private static readonly byte[] SignatureOmitted = new byte[lenT - 4];
		internal CheckSignedTimestampResult CheckSignature(SignatureOfTimestamp signatureOfTimestamp, uint fromIp, bool checkFromIp, Node node, byte[] hashBytes = null)
		{
			hashBytes = hashBytes ?? Hash();
			if (checkFromIp && signatureOfTimestamp.IpNode != fromIp)
				return CheckSignedTimestampResult.SignaturesNotFromCorrectNode;
			if (signatureOfTimestamp.Signature == SignatureOmitted)
				return CheckSignedTimestampResult.SignatureOmitted;
			if (!node.Rsa.VerifyHash(hashBytes, System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"), signatureOfTimestamp.Signature))
				return CheckSignedTimestampResult.InvalidSignature;
			return CheckSignedTimestampResult.Ok;
		}
		internal bool CheckNodeThatStartedDistributingTheObject(Node fromNode)
		{
			try
			{
				var first = ReadSignedTimespan(Convert.FromBase64String(TimestampSignature)).First();
				return fromNode.Ip == first.IpNode && fromNode.Rsa.VerifyHash(Hash(), System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"), first.Signature);
			}
			catch
			{
				return false;
			}
		}
		private const int lenT = 132;
		private static List<SignatureOfTimestamp> ReadSignedTimespan(byte[] signedTimestamps)
		{
			var ts = new List<byte[]>();
			var timestampSignatures = new List<SignatureOfTimestamp>();
			do
			{
				var timestamp = signedTimestamps.Take(lenT).ToArray();
				ts.Add(timestamp);
				signedTimestamps = signedTimestamps.Skip(lenT).ToArray();
			} while (signedTimestamps.Count() != 0);
			foreach (var signedTs in ts)
				timestampSignatures.Add(new SignatureOfTimestamp(signedTs, out _));
			return timestampSignatures;
		}
		internal class SignatureOfTimestamp
		{
			public SignatureOfTimestamp(byte[] signature, out CheckSignedTimestampResult check)
			{
				if (signature == null || signature.Length == 0)
				{
					check = CheckSignedTimestampResult.SignatureOmitted;
					return;
				}
				check = signature.Length == lenT ? CheckSignedTimestampResult.Ok : CheckSignedTimestampResult.WrongSignatureSize;
				try
				{
					IpNode = Converter.BytesToUint(signature.Take(4).ToArray());
					Signature = signature.Skip(4).ToArray();
				}
				catch { }
			}
			public uint IpNode;
			public byte[] Signature;
		}
	}
}
