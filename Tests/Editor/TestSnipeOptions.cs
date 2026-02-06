using System.Collections.Generic;
using NUnit.Framework;
using MiniIT.Snipe.Configuration;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestSnipeOptions
	{
		[Test]
		public void TestGetValidIndex()
		{
			List<int> list = null;
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 0, false));
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 1, false));
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 10, false));
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 0, true));
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 1, true));
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 10, true));

			list = new List<int>();
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 0, false));
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 1, false));
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 10, false));
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 0, true));
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 1, true));
			Assert.AreEqual(-1, SnipeOptions.GetValidIndex(list, 10, true));

			list = new List<int>() { 10, 11, 12, };
			Assert.AreEqual(0, SnipeOptions.GetValidIndex(list, 0, false));
			Assert.AreEqual(1, SnipeOptions.GetValidIndex(list, 1, false));
			Assert.AreEqual(2, SnipeOptions.GetValidIndex(list, 2, false));
			Assert.AreEqual(0, SnipeOptions.GetValidIndex(list, 3, false));
			Assert.AreEqual(0, SnipeOptions.GetValidIndex(list, 10, false));

			Assert.AreEqual(1, SnipeOptions.GetValidIndex(list, 0, true));
			Assert.AreEqual(2, SnipeOptions.GetValidIndex(list, 1, true));
			Assert.AreEqual(0, SnipeOptions.GetValidIndex(list, 2, true));
			Assert.AreEqual(0, SnipeOptions.GetValidIndex(list, 3, true));
			Assert.AreEqual(0, SnipeOptions.GetValidIndex(list, 10, true));
		}

		[Test]
		public void TestParseWebSocketUrls_WithEmptyUrls()
		{
			List<string> outputList = new List<string>();
			object input = new List<string>();
			SnipeOptionsBuilder.ParseWebSocketUrls(outputList, input);
			Assert.AreEqual(0, outputList.Count);
		}

		[Test]
		public void TestParseWebSocketUrls_WithValidUrlsInList()
		{
			List<string> outputList = new List<string>();
			object input = new List<string> { "wss://test1.com", "wss://test2.com" };
			SnipeOptionsBuilder.ParseWebSocketUrls(outputList, input);
			Assert.AreEqual(2, outputList.Count);
		}

		[Test]
		public void TestParseWebSocketUrls_WithInvalidUrlsInList()
		{
			List<string> outputList = new List<string>();
			object input = new List<string> { "test1.com", "wss://test2.com" };
			SnipeOptionsBuilder.ParseWebSocketUrls(outputList, input);
			Assert.AreEqual(1, outputList.Count);
		}

		[Test]
		public void TestParseWebSocketUrls_WithValidUrlInString()
		{
			List<string> outputList = new List<string>();
			object input = "wss://test.com";
			SnipeOptionsBuilder.ParseWebSocketUrls(outputList, input);
			Assert.AreEqual(1, outputList.Count);
		}

		[Test]
		public void TestParseWebSocketUrls_WithValidUrlInStringCaseInsensitive()
		{
			List<string> outputList = new List<string>();
			object input = "wSs://test.com";
			SnipeOptionsBuilder.ParseWebSocketUrls(outputList, input);
			Assert.AreEqual(1, outputList.Count);
		}

		[Test]
		public void TestParseWebSocketUrls_WithInvalidUrlInString()
		{
			List<string> outputList = new List<string>();
			object input = "invalid_url";
			SnipeOptionsBuilder.ParseWebSocketUrls(outputList, input);
			Assert.AreEqual(0, outputList.Count);
		}

		[Test]
		public void TestParseHttpUrls_WithEmptyUrls()
		{
			List<string> outputList = new List<string>();
			object input = new List<string>();
			SnipeOptionsBuilder.ParseHttpUrls(outputList, input);
			Assert.AreEqual(0, outputList.Count);
		}

		[Test]
		public void TestParseHttpUrls_WithWssUrlsInList()
		{
			List<string> outputList = new List<string>();
			object input = new List<string> { "wss://test1.com", "wss://test2.com" };
			SnipeOptionsBuilder.ParseHttpUrls(outputList, input);
			Assert.AreEqual(0, outputList.Count);
		}

		[Test]
		public void TestParseHttpUrls_WithValidUrlsInList()
		{
			List<string> outputList = new List<string>();
			object input = new List<string> { "https://test1.com", "https://test2.com" };
			SnipeOptionsBuilder.ParseHttpUrls(outputList, input);
			Assert.AreEqual(2, outputList.Count);
		}

		[Test]
		public void TestParseHttpUrls_WithhttpUrlsInList()
		{
			List<string> outputList = new List<string>();
			object input = new List<string> { "http://test1.com", "http://test2.com" };
			SnipeOptionsBuilder.ParseHttpUrls(outputList, input);
			Assert.AreEqual(0, outputList.Count);
		}

		[Test]
		public void TestParseHttpUrls_WithInvalidUrlsInList()
		{
			List<string> outputList = new List<string>();
			object input = new List<string> { "test1.com", "https://test2.com" };
			SnipeOptionsBuilder.ParseHttpUrls(outputList, input);
			Assert.AreEqual(1, outputList.Count);
		}

		[Test]
		public void TestParseHttpUrls_WithValidWssUrlInString()
		{
			List<string> outputList = new List<string>();
			object input = "wss://test.com";
			SnipeOptionsBuilder.ParseHttpUrls(outputList, input);
			Assert.AreEqual(0, outputList.Count);
		}

		[Test]
		public void TestParseHttpUrls_WithValidUrlInString()
		{
			List<string> outputList = new List<string>();
			object input = "https://test.com";
			SnipeOptionsBuilder.ParseHttpUrls(outputList, input);
			Assert.AreEqual(1, outputList.Count);
		}

		[Test]
		public void TestParseHttpUrls_WithValidWssUrlInStringCaseInsensitive()
		{
			List<string> outputList = new List<string>();
			object input = "wSs://test.com";
			SnipeOptionsBuilder.ParseHttpUrls(outputList, input);
			Assert.AreEqual(0, outputList.Count);
		}

		[Test]
		public void TestParseHttpUrls_WithValidUrlInStringCaseInsensitive()
		{
			List<string> outputList = new List<string>();
			string input = "HtTpS://test.com";
			SnipeOptionsBuilder.ParseHttpUrls(outputList, input);
			Assert.AreEqual(1, outputList.Count);
			Assert.AreEqual("https://test.com", outputList[0].ToLower());
		}

		[Test]
		public void TestParseHttpUrls_WithInvalidUrlInString()
		{
			List<string> outputList = new List<string>();
			object input = "invalid_url";
			SnipeOptionsBuilder.ParseHttpUrls(outputList, input);
			Assert.AreEqual(0, outputList.Count);
		}

		[Test]
		public void TestParseUdpUrls_WithEmptyUrls()
		{
			var outputList = new List<UdpAddress>();
			object input = new List<string>();
			SnipeOptionsBuilder.ParseUdpUrls(outputList, input);
			Assert.AreEqual(0, outputList.Count);
		}

		[Test]
		public void TestParseUdpUrls_WithValidUrlsInList()
		{
			var outputList = new List<UdpAddress>();
			object input = new List<string> { "https://test1.com:100", "http://test2.com/:777" };
			SnipeOptionsBuilder.ParseUdpUrls(outputList, input);
			Assert.AreEqual(2, outputList.Count);
			Assert.AreEqual(outputList[0].Host, "test1.com");
			Assert.AreEqual(outputList[0].Port, 100);
			Assert.AreEqual(outputList[1].Host, "test2.com/");
			Assert.AreEqual(outputList[1].Port, 777);
		}

		[Test]
		public void TestParseUdpUrls_WithValidNoHttpUrlsInList()
		{
			var outputList = new List<UdpAddress>();
			object input = new List<string> { "test1.com:100", "test2.com/:777" };
			SnipeOptionsBuilder.ParseUdpUrls(outputList, input);
			Assert.AreEqual(2, outputList.Count);
			Assert.AreEqual(outputList[0].Host, "test1.com");
			Assert.AreEqual(outputList[0].Port, 100);
			Assert.AreEqual(outputList[1].Host, "test2.com/");
			Assert.AreEqual(outputList[1].Port, 777);
		}

		[Test]
		public void TestParseUdpUrls_WithInvalidUrlsInList()
		{
			var outputList = new List<UdpAddress>();
			object input = new List<string> { "test1.com", "https://test2.com:99" };
			SnipeOptionsBuilder.ParseUdpUrls(outputList, input);
			Assert.AreEqual(1, outputList.Count);
			Assert.AreEqual(outputList[0].Host, "test2.com");
			Assert.AreEqual(outputList[0].Port, 99);
		}
	}
}
