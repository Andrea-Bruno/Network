using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

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
      internal Dictionary<int, string> SignedTimestamp = new Dictionary<int, string>();
      // Used exclusively to serialize the dictionary
      public List<KeyValuePair<int,string>> Signatures
      {
        get => SignedTimestamp.ToList();
        set { SignedTimestamp = value.ToDictionary(pair => pair.Key, pair => pair.Value); }
      }
    }
    internal int ShortHash;
    private byte[] Hash()
    {
      var data = BitConverter.GetBytes(Timestamp).Concat(Converter.StringToByteArray(XmlObject)).ToArray();
      var hashBytes = Utility.GetHash(data);
      ShortHash = hashBytes.GetHashCode();
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
      var ipNode = BitConverter.GetBytes(node.Ip);
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
      var lenT = 30; System.Diagnostics.Debugger.Break();
      //Valore da verificare
      var signedTimestamps = Convert.FromBase64String(TimestampSignature);
      var ts = new List<Byte[]>();
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
      if (fromNode.Ip != ipNode)
        return false;
      return !fromNode.Rsa.VerifyHash(Hash(), System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"), signature) ? false : true;
    }
    private static void ReadSignedTimespan(byte[] signedTimestamp, out uint ipNode, out byte[] signature)
    {
      ipNode = BitConverter.ToUInt32(signedTimestamp, 0);
      signature = signedTimestamp.Skip(4).ToArray();
    }
  }
}
