using System.Collections.Generic;
using System.Linq;

namespace NetworkManager
{
  public static class Utility
  {
    public static void Log(string category, string errorMessage)
    {
      System.Diagnostics.Debug.WriteLine(category);
      System.Diagnostics.Debug.Write(errorMessage);
    }
    public static string GetObjectName(string xmlObject)
    {
      if (!string.IsNullOrEmpty(xmlObject))
      {
        var p1 = xmlObject.IndexOf('>');
        if (p1 != -1)
        {
          var p2 = xmlObject.IndexOf('<', p1);
          if (p2 != -1)
          {
            var p3 = xmlObject.IndexOf('>', p2);
            if (p3 != -1)
            {
              return xmlObject.Substring(p2 + 1, p3 - p2 - 1);
            }
          }
        }
      }
      return null;
    }

    public static IEnumerable<IEnumerable<T>> GetPermutations<T>(IEnumerable<T> list, int length)
    {
      var enumerable = list as T[] ?? list.ToArray();
      return length == 1
        ? enumerable.Select(t => new[] { t })
        : GetPermutations(enumerable, length - 1).SelectMany(t => enumerable.Where(e => !t.Contains(e)), (t1, t2) => t1.Concat(new[] { t2 }));
    }

    public static byte[] GetHash(byte[] data)
    {
      System.Security.Cryptography.HashAlgorithm hashType = new System.Security.Cryptography.SHA256Managed();
      return hashType.ComputeHash(data);
    }
  }

}

