using System;
using System.Linq;

namespace NetworkManager
{
	public static class Converter
	{
		public static uint IpToUint(string ip)
		{
			var address = System.Net.IPAddress.Parse(ip);
			var bytes = address.GetAddressBytes();
			return BytesToUint(bytes);
		}
		public static string UintToIp(uint ip)
		{
			var bytes = GetBytes(ip);
			return new System.Net.IPAddress(bytes).ToString();
		}
		public static string StringToBase64(string text)
		{
			// This function is a quick way to crypt a text string
			var bytes = StringToByteArray(text);
			return Convert.ToBase64String(bytes);
		}
		public static string Base64ToString(string text)
		{
			// Now easy to decrypt a data
			var bytes = Convert.FromBase64String(text);
			return ByteArrayToString(bytes);
		}
		public static byte[] StringToByteArray(string text)
		{
			return !string.IsNullOrEmpty(text) ? System.Text.Encoding.GetEncoding("utf-16LE").GetBytes(text) : null;
		}
		public static string ByteArrayToString(byte[] bytes)
		{
			return System.Text.Encoding.GetEncoding("utf-16LE").GetString(bytes);// Unicode encoding
		}
		public static bool XmlToObject(string xml, Type type, out object obj)
		{
			var xmlSerializer = new System.Xml.Serialization.XmlSerializer(type);
			try
			{
				obj = xmlSerializer.Deserialize(new System.IO.StringReader(xml));
				return true;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.Print(ex.Message);
				System.Diagnostics.Debugger.Break();
			}
			obj = null;
			return false;
		}
		public static string ObjectToXml(object obj)
		{
			var str = new System.IO.StringWriter();
			var xml = new System.Xml.Serialization.XmlSerializer(obj.GetType());
			var xmlns = new System.Xml.Serialization.XmlSerializerNamespaces();
			xmlns.Add(string.Empty, string.Empty);
			xml.Serialize(str, obj, xmlns);
			return str.ToString();
		}
		public static byte[] GetBytes(int n)
		{
			return BitConverter.IsLittleEndian ? BitConverter.GetBytes(n).Reverse().ToArray() : BitConverter.GetBytes(n); // flip big-endian(network order) to little-endian
		}
		public static byte[] GetBytes(uint n)
		{
			return BitConverter.IsLittleEndian ? BitConverter.GetBytes(n).Reverse().ToArray(): BitConverter.GetBytes(n); // flip big-endian(network order) to little-endian
		}
		public static byte[] GetBytes(long n)
		{
			return BitConverter.IsLittleEndian ? BitConverter.GetBytes(n).Reverse().ToArray() : BitConverter.GetBytes(n); // flip big-endian(network order) to little-endian
		}
		public static byte[] GetBytes(ulong n)
		{
			return BitConverter.IsLittleEndian ? BitConverter.GetBytes(n).Reverse().ToArray() : BitConverter.GetBytes(n); // flip big-endian(network order) to little-endian
		}
		public static uint BytesToUint(byte[] bytes)
		{
			return BitConverter.ToUInt32(BitConverter.IsLittleEndian ? bytes.Reverse().ToArray() : bytes, 0); // flip big-endian(network order) to little-endian
		}
		public static int BytesToInt(byte[] bytes)
		{
			return BitConverter.ToInt32(BitConverter.IsLittleEndian ? bytes.Reverse().ToArray() : bytes, 0); // flip big-endian(network order) to little-endian
		}
	}

}
