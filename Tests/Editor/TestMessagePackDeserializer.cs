using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using MiniIT.MessagePack;

using IMap = System.Collections.Generic.IDictionary<string, object>;
using Map = System.Collections.Generic.Dictionary<string, object>;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestMessagePackDeserializer
	{
		[Test]
        public void Parse_ShouldHandleEmptyArray()
        {
            // Arrange
            byte[] emptyArray = new byte[] { 0x90 }; // MsgPack encoded empty array (0x90 is the code for empty array)

            // Act
            var result = MessagePackDeserializer.Parse(emptyArray);

            // Assert
            Assert.IsInstanceOf<IList>(result);
            Assert.IsEmpty((IList)result);
        }

        [Test]
        public void Parse_ShouldDecodeArrayCorrectly()
        {
            // Arrange
            byte[] arrayBytes = new byte[] { 0x93, 0x01, 0x02, 0x03 }; // MsgPack encoded array [1, 2, 3]

            // Act
            var result = MessagePackDeserializer.Parse(arrayBytes);

            // Assert
            Assert.IsInstanceOf<IList>(result);
            Assert.AreEqual(new List<int> { 1, 2, 3 }, (IList)result);
        }

        [Test]
        public void Parse_ShouldDecodeMapCorrectly()
        {
            // Arrange
            byte[] mapBytes = new byte[] { 0x81, 0xa3, 0x66, 0x6f, 0x6f, 0x01 }; // MsgPack encoded map { "foo": 1 }

            // Act
            var result = MessagePackDeserializer.Parse(mapBytes);

            // Assert
            Assert.IsInstanceOf<IDictionary<string, object>>(result);
            var resultMap = (IDictionary<string, object>)result;
            Assert.AreEqual(1, resultMap["foo"]);
        }

        [Test]
        public void Parse_ShouldReturnZeroForZeroByte()
        {
            // Arrange
            byte[] invalidBytes = new byte[] { 0x00 }; // Invalid MsgPack encoding

            // Act
            var result = MessagePackDeserializer.Parse(invalidBytes);

            // // Assert
            Assert.AreEqual(0, result);
        }

        [Test]
        public void Parse_ShouldHandleNegativeInteger()
        {
            // Arrange
            byte[] negativeIntBytes = new byte[] { 0xd1, 0xff, 0xff, 0xff, 0xff }; // MsgPack encoded -1

            // Act
            var result = MessagePackDeserializer.Parse(negativeIntBytes);

            // Assert
            Assert.AreEqual(-1, result);
        }

        [Test]
        public void Parse_ComplexMapShouldDecodeCorrectly()
        {
	        // Arrange
	        var map = new Map()
	        {
		        ["id"] = 10,
		        ["negative"] = -100,
		        ["data"] = new Map()
		        {
			        ["foo"] = 30,
			        ["bar"] = "some value",
			        ["list"] = new string[] { "one", "two", "three" },
			        ["floats"] = new List<float>() { 1.1f, 1.2f, 1.3f },
		        }
	        };
	        
	        Span<byte> mapBytes = new MessagePackSerializerNonAlloc(4096).Serialize(map);

	        // Act
	        var result = MessagePackDeserializer.Parse(mapBytes);

	        // Assert
	        Assert.IsInstanceOf<IMap>(result);
	        var resultMap = (IMap)result;
	        Assert.AreEqual(10, resultMap["id"]);

	        Assert.AreEqual(-100, resultMap["negative"]);

	        Assert.IsInstanceOf<IMap>(resultMap["data"]);
	        var submap = (IMap)resultMap["data"];
	        Assert.AreEqual(30, submap["foo"]);

	        Assert.IsInstanceOf<string>(submap["bar"]);
	        Assert.AreEqual("some value", submap["bar"]);

	        Assert.IsInstanceOf<IList>(submap["list"]);
	        var strings = (IList)submap["list"];
	        Assert.AreEqual(3, strings.Count);
	        Assert.AreEqual("one", strings[0]);
	        Assert.AreEqual("three", strings[2]);

	        Assert.IsInstanceOf<IList>(submap["floats"]);
	        var floats = (IList)submap["floats"];
	        Assert.AreEqual(3, floats.Count);
	        Assert.AreEqual(1.1f, floats[0]);
	        Assert.AreEqual(1.3f, floats[2]);
        }
	}
}
