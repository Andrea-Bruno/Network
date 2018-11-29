using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
			if (string.IsNullOrEmpty(xmlObject)) return null;
			var p1 = xmlObject.IndexOf('>');
			if (p1 == -1) return null;
			var p2 = xmlObject.IndexOf('<', p1);
			if (p2 == -1) return null;
			var p3 = xmlObject.IndexOf('>', p2);
			return p3 != -1 ? xmlObject.Substring(p2 + 1, p3 - p2 - 1) : null;
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

		public static string MinifyXml(string xml)
		{
			var result = "";
			xml.Replace("\r", "").Split('\n').ToList().ForEach(x => result += x.TrimStart());
			return result;
		}

		public static bool GetAverageDateTimeFromWeb(out DateTime dateTime, out TimeSpan delta)
		{
			var webs = new[] {
				"https://foundation.mozilla.org/",
				"https://www.timeanddate.com/",
				"https://www.time.gov/",
				"http://www.wikipedia.org/",
				"https://www.facebook.com/",
				"https://www.linuxfoundation.org/",
				"https://m.youtube.com/",
				"https://www.vk.com/",
				"https://www.amazon.com/",
				"https://www.google.com/",
				"https://www.microsoft.com/",
				"https://nist.time.gov/",
				"https://www.google.co.in/",
				"https://www.rolex.com/",
				"https://creativecommons.org/"
			};
			var deltas = new List<TimeSpan>();
			for (var i = 1; i <= 1; i++)
				Parallel.ForEach(webs, web =>
				{
					var time = GetDateTimeFromWeb(web);
					if (time != null)
						deltas.Add(DateTime.UtcNow - (DateTime)time);
				});
			dateTime = DateTime.UtcNow;
			if (deltas.Count == 0) return false;
			var middle = deltas.Count / 2;
			delta = deltas.Count % 2 == 0 ? new TimeSpan(deltas[middle].Ticks / 2 + deltas[middle + 1].Ticks / 2) : deltas[middle];
			dateTime = DateTime.UtcNow.Add(delta);
			return true;
		}

		private static DateTime? GetDateTimeFromWeb(string fromWebsite)
		{
			using (var client = new HttpClient())
			{
				try
				{
					var result = client.GetAsync(fromWebsite, HttpCompletionOption.ResponseHeadersRead).Result;
					if (result.Headers?.Date != null)
						return result.Headers?.Date.Value.UtcDateTime.AddMilliseconds(366); // for stats the time of website have a error of 366 ms; 					
				}
				catch
				{
					// ignored
				}
				return null;
			}
		}

		public static bool GetNistTime(out DateTime dateTime, out TimeSpan delta)
		{			
			try
			{
				var request = (HttpWebRequest)System.Net.WebRequest.Create("http://nist.time.gov/actualtime.cgi?lzbc=siqm9b");
				request.Method = "GET";
				request.Accept = "text/html, application/xhtml+xml, */*";
				request.UserAgent = "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.1; Trident/6.0)";
				request.ContentType = "application/x-www-form-urlencoded";
				request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore); //No caching
				var response = (HttpWebResponse)request.GetResponse();
				if (response.StatusCode != HttpStatusCode.OK) {dateTime = DateTime.UtcNow; return false;}
				var stream = new StreamReader(response.GetResponseStream() ?? throw new InvalidOperationException());
				var html = stream.ReadToEnd();
				var time = Regex.Match(html, @"(?<=\btime="")[^""]*").Value;
				var milliseconds = Convert.ToInt64(time) / 1000.0;
				dateTime = new DateTime(1970, 1, 1).AddMilliseconds(milliseconds);
				delta = DateTime.UtcNow - dateTime;
				return true;
			}
			catch
			{
				dateTime = DateTime.UtcNow;
				return false;
			}
		}
	}

}

