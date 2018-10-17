using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkManager
{
  public static class Utility
  {
    public static void Log(string Category, int MaxRecord, string ErrorMessage)
    {
      System.Diagnostics.Debug.WriteLine(Category);
      System.Diagnostics.Debug.Write(ErrorMessage);
    }
    public static string GetObjectName(string XmlObject)
    {
      if (!string.IsNullOrEmpty(XmlObject))
      {
        var p1 = XmlObject.IndexOf('>');
        if (p1 != -1)
        {
          var p2 = XmlObject.IndexOf('<', p1);
          if (p2 != -1)
          {
            var p3 = XmlObject.IndexOf('>', p2);
            if (p3 != -1)
            {
              return XmlObject.Substring(p2 + 1, p3 - p2 - 1);
            }
          }
        }
      }
      return null;
    }
  }
}
