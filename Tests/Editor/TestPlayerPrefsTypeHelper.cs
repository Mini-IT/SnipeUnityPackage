using System.Collections.Generic;
using NUnit.Framework;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestPlayerPrefsTypeHelper
	{
		private MockSharedPrefs _sharedPrefs;
		private PlayerPrefsTypeHelper _helper;

		[SetUp]
		public void SetUp()
		{
			_sharedPrefs = new MockSharedPrefs();
			_helper = new PlayerPrefsTypeHelper(_sharedPrefs);
		}

		#region Primitive Types - Set and Get

		[Test]
		public void SetAndGet_Int_Success()
		{
			_helper.SetLocalValue("key", 42);
			int result = _helper.GetPrefsValue<int>("key");
			Assert.AreEqual(42, result);
		}

		[Test]
		public void SetAndGet_Byte_Success()
		{
			_helper.SetLocalValue("key", (byte)255);
			byte result = _helper.GetPrefsValue<byte>("key");
			Assert.AreEqual(255, result);
		}

		[Test]
		public void SetAndGet_Short_Success()
		{
			_helper.SetLocalValue("key", (short)32767);
			short result = _helper.GetPrefsValue<short>("key");
			Assert.AreEqual(32767, result);
		}

		[Test]
		public void SetAndGet_UShort_Success()
		{
			_helper.SetLocalValue("key", (ushort)65535);
			ushort result = _helper.GetPrefsValue<ushort>("key");
			Assert.AreEqual(65535, result);
		}

		[Test]
		public void SetAndGet_Long_Success()
		{
			_helper.SetLocalValue("key", 9999999L);
			long result = _helper.GetPrefsValue<long>("key");
			Assert.AreEqual(9999999L, result);
		}

		[Test]
		public void SetAndGet_ULong_Success()
		{
			_helper.SetLocalValue("key", 9999999UL);
			ulong result = _helper.GetPrefsValue<ulong>("key");
			Assert.AreEqual(9999999UL, result);
		}

		[Test]
		public void SetAndGet_Float_Success()
		{
			_helper.SetLocalValue("key", 3.14f);
			float result = _helper.GetPrefsValue<float>("key");
			Assert.AreEqual(3.14f, result, 0.0001f);
		}

		[Test]
		public void SetAndGet_Double_Success()
		{
			_helper.SetLocalValue("key", 3.14159);
			double result = _helper.GetPrefsValue<double>("key");
			Assert.AreEqual(3.14159, result, 0.0001);
		}

		[Test]
		public void SetAndGet_Bool_True()
		{
			_helper.SetLocalValue("key", true);
			bool result = _helper.GetPrefsValue<bool>("key");
			Assert.AreEqual(true, result);
		}

		[Test]
		public void SetAndGet_Bool_False()
		{
			_helper.SetLocalValue("key", false);
			bool result = _helper.GetPrefsValue<bool>("key");
			Assert.AreEqual(false, result);
		}

		[Test]
		public void SetAndGet_String_Success()
		{
			_helper.SetLocalValue("key", "Hello World");
			string result = _helper.GetPrefsValue<string>("key");
			Assert.AreEqual("Hello World", result);
		}

		#endregion

		#region Default Values

		[Test]
		public void GetPrefsValue_NonExistentKey_ReturnsDefault()
		{
			int result = _helper.GetPrefsValue<int>("nonexistent");
			Assert.AreEqual(0, result);
		}

		[Test]
		public void GetPrefsValue_NonExistentKey_WithDefaultValue_ReturnsDefault()
		{
			int result = _helper.GetPrefsValue("nonexistent", 99);
			Assert.AreEqual(99, result);
		}

		[Test]
		public void GetPrefsValue_String_NonExistentKey_ReturnsEmpty()
		{
			string result = _helper.GetPrefsValue<string>("nonexistent");
			Assert.AreEqual("", result);
		}

		[Test]
		public void GetPrefsValue_String_WithDefaultValue()
		{
			string result = _helper.GetPrefsValue("nonexistent", "default");
			Assert.AreEqual("default", result);
		}

		#endregion

		#region List<int> and Alternative Integer Types

		[Test]
		public void SetAndGet_ListInt_Success()
		{
			var list = new List<int> { 1, 2, 3, 42, -5 };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<int>>("key");
			CollectionAssert.AreEqual(list, result);
		}

		[Test]
		public void SetAndGet_ListByte_Success()
		{
			var list = new List<byte> { 0, 127, 255 };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<byte>>("key");
			CollectionAssert.AreEqual(list, result);
		}

		[Test]
		public void SetAndGet_ListShort_Success()
		{
			var list = new List<short> { -32768, 0, 32767 };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<short>>("key");
			CollectionAssert.AreEqual(list, result);
		}

		[Test]
		public void SetAndGet_ListUShort_Success()
		{
			var list = new List<ushort> { 0, 1000, 65535 };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<ushort>>("key");
			CollectionAssert.AreEqual(list, result);
		}

		[Test]
		public void SetAndGet_ListLong_Success()
		{
			var list = new List<long> { -9999999L, 0, 9999999L };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<long>>("key");
			CollectionAssert.AreEqual(list, result);
		}

		[Test]
		public void SetAndGet_ListULong_Success()
		{
			var list = new List<ulong> { 0, 9999999UL, 18446744073709551615UL };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<ulong>>("key");
			CollectionAssert.AreEqual(list, result);
		}

		#endregion

		#region List<float> and Alternative Float Types

		[Test]
		public void SetAndGet_ListFloat_Success()
		{
			var list = new List<float> { 1.5f, 2.7f, -3.14f };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<float>>("key");
			Assert.AreEqual(list.Count, result.Count);
			for (int i = 0; i < list.Count; i++)
			{
				Assert.AreEqual(list[i], result[i], 0.0001f);
			}
		}

		[Test]
		public void SetAndGet_ListDouble_Success()
		{
			var list = new List<double> { 1.5, 2.7, -3.14159 };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<double>>("key");
			Assert.AreEqual(list.Count, result.Count);
			for (int i = 0; i < list.Count; i++)
			{
				Assert.AreEqual(list[i], result[i], 0.0001);
			}
		}

		[Test]
		public void SetAndGet_ListDecimal_Success()
		{
			var list = new List<decimal> { 1.5m, 2.7m, -3.14159m };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<decimal>>("key");
			Assert.AreEqual(list.Count, result.Count);
			for (int i = 0; i < list.Count; i++)
			{
				Assert.AreEqual(list[i], result[i]);
			}
		}

		#endregion

		#region List<bool> and List<string>

		[Test]
		public void SetAndGet_ListBool_Success()
		{
			var list = new List<bool> { true, false, true, true };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<bool>>("key");
			CollectionAssert.AreEqual(list, result);
		}

		[Test]
		public void SetAndGet_ListString_Success()
		{
			var list = new List<string> { "Hello", "World", "Test" };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<string>>("key");
			CollectionAssert.AreEqual(list, result);
		}

		[Test]
		public void SetAndGet_ListString_WithSpecialCharacters()
		{
			var list = new List<string> { "Hello;World", "Test\"Quote", "Back\\slash" };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<string>>("key");
			CollectionAssert.AreEqual(list, result);
		}

		[Test]
		public void SetAndGet_ListString_WithEmptyString()
		{
			var list = new List<string> { "Hello", "", "World" };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<string>>("key");
			CollectionAssert.AreEqual(list, result);
		}

		#endregion

		#region Empty and Null Lists

		[Test]
		public void SetAndGet_EmptyListInt_ReturnsEmpty()
		{
			var list = new List<int>();
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<int>>("key");
			Assert.AreEqual(0, result.Count);
		}

		[Test]
		public void SetAndGet_NullList_StoresEmpty()
		{
			_helper.SetLocalValue("key", (List<int>)null);
			var result = _helper.GetPrefsValue<List<int>>("key");
			Assert.AreEqual(0, result.Count);
		}

		[Test]
		public void GetPrefsValue_ListInt_NonExistentKey_ReturnsEmpty()
		{
			var result = _helper.GetPrefsValue<List<int>>("nonexistent");
			Assert.AreEqual(0, result.Count);
		}

		[Test]
		public void GetPrefsValue_ListInt_WithDefaultValue()
		{
			var defaultList = new List<int> { 10, 20, 30 };
			var result = _helper.GetPrefsValue("nonexistent", defaultList);
			CollectionAssert.AreEqual(defaultList, result);
		}

		[Test]
		public void GetPrefsValue_ListInt_ExistingKey_IgnoresDefaultValue()
		{
			var list = new List<int> { 1, 2, 3 };
			var defaultList = new List<int> { 10, 20, 30 };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue("key", defaultList);
			CollectionAssert.AreEqual(list, result);
		}

		#endregion

		#region Edge Cases

		[Test]
		public void SetAndGet_ListInt_WithNegativeNumbers()
		{
			var list = new List<int> { -100, -50, 0, 50, 100 };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<int>>("key");
			CollectionAssert.AreEqual(list, result);
		}

		[Test]
		public void SetAndGet_ListFloat_WithNegativeNumbers()
		{
			var list = new List<float> { -100.5f, -50.2f, 0f, 50.7f, 100.9f };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<float>>("key");
			Assert.AreEqual(list.Count, result.Count);
			for (int i = 0; i < list.Count; i++)
			{
				Assert.AreEqual(list[i], result[i], 0.0001f);
			}
		}

		[Test]
		public void SetAndGet_ListDouble_WithVerySmallNumbers()
		{
			var list = new List<double> { 0.0000001, 0.0000002, 0.0000003 };
			_helper.SetLocalValue("key", list);
			var result = _helper.GetPrefsValue<List<double>>("key");
			Assert.AreEqual(list.Count, result.Count);
			for (int i = 0; i < list.Count; i++)
			{
				Assert.AreEqual(list[i], result[i], 0.00000001);
			}
		}

		[Test]
		public void SetAndGet_MaxValues_AllIntegerTypes()
		{
			_helper.SetLocalValue("byte", byte.MaxValue);
			_helper.SetLocalValue("short", short.MaxValue);
			_helper.SetLocalValue("ushort", ushort.MaxValue);

			Assert.AreEqual(byte.MaxValue, _helper.GetPrefsValue<byte>("byte"));
			Assert.AreEqual(short.MaxValue, _helper.GetPrefsValue<short>("short"));
			Assert.AreEqual(ushort.MaxValue, _helper.GetPrefsValue<ushort>("ushort"));
		}

		[Test]
		public void SetAndGet_MinValues_AllIntegerTypes()
		{
			_helper.SetLocalValue("byte", byte.MinValue);
			_helper.SetLocalValue("short", short.MinValue);
			_helper.SetLocalValue("ushort", ushort.MinValue);

			Assert.AreEqual(byte.MinValue, _helper.GetPrefsValue<byte>("byte"));
			Assert.AreEqual(short.MinValue, _helper.GetPrefsValue<short>("short"));
			Assert.AreEqual(ushort.MinValue, _helper.GetPrefsValue<ushort>("ushort"));
		}

		#endregion
	}
}
