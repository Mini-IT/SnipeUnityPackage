using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MiniIT;
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
}
