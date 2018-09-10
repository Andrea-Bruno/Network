using System;
using System.Collections.Generic;


namespace NetworkManager
{
  public static class Converter
  {
    public static string StringToBase64(string Text)
    {
      // This function is a quick way to crypt a text string
      byte[] Bytes = StringToByteArray(Text);
      return System.Convert.ToBase64String(Bytes);
    }
    public static string Base64ToString(string Text)
    {
      // Now easy to decrypt a data
      byte[] Bytes = System.Convert.FromBase64String(Text);
      return ByteArrayToString(Bytes);
    }
    public static byte[] StringToByteArray(string Text)
    {
      if (!string.IsNullOrEmpty(Text))
      {
        // The object System.Text.Encoding.Unicode have a problem in Windows x64. Replache this object with System.Text.Encoding.GetEncoding("utf-16LE") 
        return System.Text.Encoding.GetEncoding("utf-16LE").GetBytes(Text);// Unicode encoding
      }
      return null;
    }
    public static string ByteArrayToString(byte[] Bytes)
    {
      return System.Text.Encoding.GetEncoding("utf-16LE").GetString(Bytes);// Unicode encodin
    }
    public static bool XmlToObject(string Xml, Type Type, out object Obj)
    {
      System.Xml.Serialization.XmlSerializer XmlSerializer = new System.Xml.Serialization.XmlSerializer(Type);
      try
      {
        Obj = XmlSerializer.Deserialize(new System.IO.StringReader(Xml));
        return true;
      }
      catch (Exception ex)
      {
        Obj = null;
        return false;
      }
    }
  }

}
