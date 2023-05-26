using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MiniIT;

public class TestTypeConverter
{
	[Test]
	public void Convert_NullValue_ValueTypeReturnsZero()
	{
		object val = null;
		int result = TypeConverter.Convert<int>(val);
		Assert.AreEqual(0, result);
	}

	[Test]
	public void Convert_NullValue_ValueTypeReturnsFalse()
	{
		object val = null;
		bool result = TypeConverter.Convert<bool>(val);
		Assert.AreEqual(false, result);
	}

	[Test]
	public void Convert_NullValue_ReferenceTypeReturnsDefault()
	{
		object val = null;
		string result = TypeConverter.Convert<string>(val);
		Assert.AreEqual(null, result);
	}

	[Test]
	public void Convert_String_ReturnsString()
	{
		string val = "somestring";
		string result = TypeConverter.Convert<string>(val);
		Assert.AreEqual(val, result);
	}

	[Test]
	public void Convert_ObjectType_ConvertsCorrectly()
	{
		object val = "example";
		object result = TypeConverter.Convert<object>(val);
		Assert.AreEqual(val, result);
	}

	[Test]
	public void Convert_Collection_ConvertsToList()
	{
		var val = new List<int> { 1, 2, 3 };
		List<int> result = TypeConverter.Convert<List<int>>(val);
		CollectionAssert.AreEqual(val, result);
	}

	[Test]
	public void Convert_ListOfStrings_ConvertsToList()
	{
		var val = new List<string> { "1", "2", "3" };
		List<string> result = TypeConverter.Convert<List<string>>(val);
		CollectionAssert.AreEqual(val, result);
	}

	[Test]
	public void Convert_ValueType_ConvertsCorrectly()
	{
		object val = 42;
		int result = TypeConverter.Convert<int>(val);
		Assert.AreEqual(42, result);
	}

	[Test]
	public void Convert_Int_ConvertsToFloat()
	{
		int val = 52;
		float result = TypeConverter.Convert<float>(val);
		Assert.AreEqual(52, result);
	}

	[Test]
	public void Convert_Float_ConvertsToInt()
	{
		float val = 32.0f;
		int result = TypeConverter.Convert<int>(val);
		Assert.AreEqual(32, result);
	}
}
