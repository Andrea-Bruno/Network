using System;
using System.Collections.Generic;

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
      var bytes = BitConverter.GetBytes(n);
      if (BitConverter.IsLittleEndian) Array.Reverse(bytes); // flip big-endian(network order) to little-endian
      return bytes;
    }
    public static byte[] GetBytes(uint n)
    {
      var bytes = BitConverter.GetBytes(n);
      if (BitConverter.IsLittleEndian) Array.Reverse(bytes); // flip big-endian(network order) to little-endian
      return bytes;
    }
    public static byte[] GetBytes(long n)
    {
      var bytes = BitConverter.GetBytes(n);
      if (BitConverter.IsLittleEndian) Array.Reverse(bytes); // flip big-endian(network order) to little-endian
      return bytes;
    }
    public static byte[] GetBytes(ulong n)
    {
      var bytes = BitConverter.GetBytes(n);
      if (BitConverter.IsLittleEndian) Array.Reverse(bytes); // flip big-endian(network order) to little-endian
      return bytes;
    }
    public static uint BytesToUint(byte[] bytes)
    {
      if (BitConverter.IsLittleEndian)
        Array.Reverse(bytes); // flip big-endian(network order) to little-endian
      return BitConverter.ToUInt32(bytes, 0);
    }
    public static int BytesToInt(byte[] bytes)
    {
      if (BitConverter.IsLittleEndian)
        Array.Reverse(bytes); // flip big-endian(network order) to little-endian
      return BitConverter.ToInt32(bytes, 0);
    }
  }

}
