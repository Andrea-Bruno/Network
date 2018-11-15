using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;

namespace NetworkManager
{
    public class Element
    {
        public long Timestamp;
        public string XmlObject;
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
            /// <summary>
            /// "int" is the short hash of ObjToNode and "string" is the SignedTimestamp for the single ObjToNode
            /// </summary>
            [XmlIgnore]
            public readonly Dictionary<int, string> SignedTimestamp = new Dictionary<int, string>();
            // Used exclusively to serialize the dictionary
            public List<Signature> SignedTimestampList
            {
                get
                {
                    var result = new List<Signature>();
                    foreach (var item in SignedTimestamp)
                    {
                        result.Add(new Signature() { Hash = item.Key, Sign = item.Value });
                    }
                    return result;
                }
                set { foreach (var keyValue in value) { SignedTimestamp.Add(keyValue.Hash, keyValue.Sign); } }
            }
            public class Signature
            {
                public int Hash;
                public string Sign;
            }
        }
        private int _shortHash;
        internal int ShortHash()
        {
            if (_shortHash == 0)
                Hash();
            return _shortHash;
        }

        private byte[] Hash()
        {
            var data = Converter.GetBytes(Timestamp).Concat(Converter.StringToByteArray(XmlObject)).ToArray();
            var hashBytes = Utility.GetHash(data);
            _shortHash = hashBytes.GetHashCode();
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
            var thisMoment = now;
            var dt = new DateTime(Timestamp);
            const double margin = 0.5; // ***Calculates a margin because the clocks on the nodes may not be perfectly synchronized
            if (thisMoment < dt.AddSeconds(-margin))
            {
                Utility.Log("signature", "signature rejected for incongruous timestamp");
                System.Diagnostics.Debugger.Break();
                return null;
            }
            const int maximumTimeToTransmitTheDataOnTheNode = 2; // ***In seconds
            return thisMoment <= dt.AddSeconds(maximumTimeToTransmitTheDataOnTheNode + margin) ? GetTimestampSignature(myNode) : null;
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
        }
        internal enum CheckSignedTimestampResult { Ok, NodeThatPutTheSignatureNotFound, InvalidSignature, NonCompliantNetworkConfiguration }
        /// <summary>
        /// Check if all the nodes responsible for distributing this data have put their signature on the timestamp
        /// </summary>
        /// <param name="network"></param>
        /// <returns>Result of the operation</returns>
        internal CheckSignedTimestampResult CheckSignedTimestamp(Network network)
        {
            var currentNodeList = network.NodeList.CurrentAndRecentNodes();
            var lenT = 30; System.Diagnostics.Debugger.Break();//***
                                                               //Valore da verificare
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
                    firstNode = node;
                else
                    nodes.Add(node);
                if (!node.Rsa.VerifyHash(hashBytes, System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"), signature))
                    return CheckSignedTimestampResult.InvalidSignature;
            }
            return network.ValidateConnectionAtLevel0(firstNode, nodes)
              ? CheckSignedTimestampResult.Ok
              : CheckSignedTimestampResult.NonCompliantNetworkConfiguration;
        }
        internal bool CheckNodeThatStartedDistributingTheObject(Node fromNode)
        {
            ReadSignedTimespan(Convert.FromBase64String(TimestampSignature), out var ipNode, out var signature);
            return fromNode.Ip == ipNode && fromNode.Rsa.VerifyHash(Hash(), System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"), signature);
        }
        private static void ReadSignedTimespan(byte[] signedTimestamp, out uint ipNode, out byte[] signature)
        {
            ipNode = Converter.BytesToUint(signedTimestamp);
            signature = signedTimestamp.Skip(4).ToArray();
        }
    }
}
