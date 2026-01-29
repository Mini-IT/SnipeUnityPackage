using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestUserAttributeAreEqual
	{
		[Test]
		public void AreEqual_BothObjectsNull_ReturnsTrue()
		{
			// Arrange
			object objA = null;
			object objB = null;

			// Act
			bool result = SnipeApiUserAttribute.AreEqual(objA, objB);

			// Assert
			Assert.IsTrue(result);
		}

		[Test]
		public void AreEqual_ObjAIsNullAndObjBEmptyList_ReturnsTrue()
		{
			// Arrange
			object objA = null;
			object objB = new List<object>();

			// Act
			bool result = SnipeApiUserAttribute.AreEqual(objA, objB);

			// Assert
			Assert.IsTrue(result);
		}

		[Test]
		public void AreEqual_ObjBIsNullAndObjAEmptyList_ReturnsTrue()
		{
			// Arrange
			object objA = new List<object>();
			object objB = null;

			// Act
			bool result = SnipeApiUserAttribute.AreEqual(objA, objB);

			// Assert
			Assert.IsTrue(result);
		}

		[Test]
		public void AreEqual_BothObjectsNotEmptyLists_ReturnsFalse()
		{
			// Arrange
			object objA = new List<object> { 1, 2, 3 };
			object objB = new List<object> { 4, 5, 6 };

			// Act
			bool result = SnipeApiUserAttribute.AreEqual(objA, objB);

			// Assert
			Assert.IsFalse(result);
		}

		[Test]
		public void AreEqual_BothObjectsSameNotEmptyLists_ReturnsTrue()
		{
			// Arrange
			object objA = new List<object> { 1, 2, 3 };
			object objB = new List<object> { 1, 2, 3 };

			// Act
			bool result = SnipeApiUserAttribute.AreEqual(objA, objB);

			// Assert
			Assert.IsTrue(result);
		}

		[Test]
		public void AreEqual_ObjectsAreDifferentTypes_ReturnsFalse()
		{
			// Arrange
			object objA = new List<object> { 1, 2, 3 };
			object objB = "Test String";

			// Act
			bool result = SnipeApiUserAttribute.AreEqual(objA, objB);

			// Assert
			Assert.IsFalse(result);
		}

		[Test]
		public void AreEqual_DifferentNumericTypesSameValue_ReturnsTrue()
		{
			// Arrange
			object byteValue = (byte)10;
			object ushortValue = (ushort)10;
			object intValue = 10;
			object longValue = 10L;

			// Act
			bool resultByteUshort = SnipeApiUserAttribute.AreEqual(byteValue, ushortValue);
			bool resultIntLong = SnipeApiUserAttribute.AreEqual(intValue, longValue);

			// Assert
			Assert.IsTrue(resultByteUshort);
			Assert.IsTrue(resultIntLong);
		}

		[Test]
		public void AreEqual_DictionariesWithDifferentValues_ReturnsFalse()
		{
			// Arrange
			object objA = new Dictionary<string, int> { ["Age"] = 25 };
			object objB = new Dictionary<string, int> { ["Age"] = 30 };

			// Act
			bool result = SnipeApiUserAttribute.AreEqual(objA, objB);

			// Assert
			Assert.IsFalse(result);
		}

		[Test]
		public void AreEqual_DictionariesWithSameValues_ReturnsTrue()
		{
			// Arrange
			object objA = new Dictionary<string, int> { ["Age"] = 25 };
			object objB = new Dictionary<string, int> { ["Age"] = 25 };

			// Act
			bool result = SnipeApiUserAttribute.AreEqual(objA, objB);

			// Assert
			Assert.IsTrue(result);
		}
	}
}
