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
  }
}
