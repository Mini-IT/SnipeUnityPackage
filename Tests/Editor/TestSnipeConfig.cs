using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MiniIT.Snipe;

public class TestSnipeConfig
{
	[Test]
	public void TestGetValidIndex()
	{
		List<int> list = null;
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 0, false));
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 1, false));
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 10, false));
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 0, true));
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 1, true));
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 10, true));
		
		list = new List<int>();
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 0, false));
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 1, false));
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 10, false));
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 0, true));
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 1, true));
		Assert.AreEqual(-1, SnipeConfig.GetValidIndex(list, 10, true));
		
		list = new List<int>() { 10, 11, 12, };
		Assert.AreEqual(0, SnipeConfig.GetValidIndex(list, 0, false));
		Assert.AreEqual(1, SnipeConfig.GetValidIndex(list, 1, false));
		Assert.AreEqual(2, SnipeConfig.GetValidIndex(list, 2, false));
		Assert.AreEqual(0, SnipeConfig.GetValidIndex(list, 3, false));
		Assert.AreEqual(0, SnipeConfig.GetValidIndex(list, 10, false));
		
		Assert.AreEqual(1, SnipeConfig.GetValidIndex(list, 0, true));
		Assert.AreEqual(2, SnipeConfig.GetValidIndex(list, 1, true));
		Assert.AreEqual(0, SnipeConfig.GetValidIndex(list, 2, true));
		Assert.AreEqual(0, SnipeConfig.GetValidIndex(list, 3, true));
		Assert.AreEqual(0, SnipeConfig.GetValidIndex(list, 10, true));
	}

	[Test]
	public void TestParseWebSocketUrls_WithEmptyUrls()
	{
		List<string> outputList = new List<string>();
		object wssUrl = new List<string>();
		SnipeConfig.ParseWebSocketUrls(outputList, wssUrl);
		Assert.AreEqual(0, outputList.Count);
	}

	[Test]
	public void TestParseWebSocketUrls_WithValidUrlsInList()
	{
		List<string> outputList = new List<string>();
		object wssUrl = new List<string> { "wss://test1.com", "wss://test2.com" };
		SnipeConfig.ParseWebSocketUrls(outputList, wssUrl);
		Assert.AreEqual(2, outputList.Count);
	}

	[Test]
	public void TestParseWebSocketUrls_WithInvalidUrlsInList()
	{
		List<string> outputList = new List<string>();
		object wssUrl = new List<string> { "test1.com", "wss://test2.com" };
		SnipeConfig.ParseWebSocketUrls(outputList, wssUrl);
		Assert.AreEqual(1, outputList.Count);
	}

	[Test]
	public void TestParseWebSocketUrls_WithValidUrlInString()
	{
		List<string> outputList = new List<string>();
		object wssUrl = "wss://test.com";
		SnipeConfig.ParseWebSocketUrls(outputList, wssUrl);
		Assert.AreEqual(1, outputList.Count);
	}

	[Test]
	public void TestParseWebSocketUrls_WithValidUrlInStringCaseInsensitive()
	{
		List<string> outputList = new List<string>();
		object wssUrl = "wSs://test.com";
		SnipeConfig.ParseWebSocketUrls(outputList, wssUrl);
		Assert.AreEqual(1, outputList.Count);
	}

	[Test]
	public void TestParseWebSocketUrls_WithInvalidUrlInString()
	{
		List<string> outputList = new List<string>();
		object wssUrl = "invalid_url";
		SnipeConfig.ParseWebSocketUrls(outputList, wssUrl);
		Assert.AreEqual(0, outputList.Count);
	}
}
