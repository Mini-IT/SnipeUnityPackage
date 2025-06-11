using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using MiniIT.MessagePack;
using MiniIT.Snipe.Unity;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestMessageSerializer
	{
		[Test]
		public void TestWSMessageSerializerMultithread()
		{
			const int THREADS_COUNT = 40;
			List<SnipeObject> data = new List<SnipeObject>(THREADS_COUNT);
			List<byte[]> serialized = new List<byte[]>(THREADS_COUNT);

			if (!SnipeServices.IsInitialized)
			{
				SnipeServices.Initialize(new UnitySnipeServicesFactory());
			}

			var serializer = new MessagePackSerializer(4096);

			for (int i = 0; i < THREADS_COUNT; i++)
			{
				var obj = GenerateRandomSnipeObject();
				data.Add(obj);
				serialized.Add(serializer.Serialize(obj).ToArray());
			}

			// Unique WebSocketTransport instances
			var config = new SnipeConfig(0);
			List<byte[]> result = Task.Run(async () => await TestWSMessageSerializerAsync(data, config)).GetAwaiter()
				.GetResult();

			Assert.AreEqual(serialized.Count, result.Count);
			for (int i = 0; i < data.Count; i++)
			{
				Assert.AreEqual(serialized[i], result[i]);
			}

			// Single WebSocketTransport instance
			var transport = new WebSocketTransport(config, null);
			result = Task.Run(async () => await TestWSMessageSerializerAsync(data, transport)).GetAwaiter().GetResult();
			Assert.AreEqual(serialized.Count, result.Count);
			for (int i = 0; i < data.Count; i++)
			{
				Assert.AreEqual(serialized[i], result[i]);
			}
		}

		private async Task<List<byte[]>> TestWSMessageSerializerAsync(List<SnipeObject> data, SnipeConfig config)
		{
			var transport = new WebSocketTransport(config, null);
			return await TestWSMessageSerializerAsync(data, transport);
		}

		private async Task<List<byte[]>> TestWSMessageSerializerAsync(List<SnipeObject> data,
			WebSocketTransport transport)
		{
			List<byte[]> result = new List<byte[]>(data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				result.Add(null);
			}

			List<Task> tasks = new List<Task>(data.Count);

			for (int i = 0; i < data.Count; i++)
			{
				int index = i;
				var task = Task.Run(async () => result[index] = await transport.SerializeMessage(data[index]));
				tasks.Add(task);
			}

			await Task.WhenAll(tasks);
			return result;
		}

		private SnipeObject GenerateRandomSnipeObject()
		{
			SnipeObject data = new SnipeObject();
			int intFieldsCount = 5;
			int stringFieldsCount = UnityEngine.Random.Range(2, 10);
			for (int i = 0; i < intFieldsCount; i++)
			{
				data[$"field{i}"] = i;
				data[Guid.NewGuid().ToString()] = i * 12 + stringFieldsCount;
			}

			for (int k = 0; k < stringFieldsCount; k++)
			{
				data[Guid.NewGuid().ToString()] = Guid.NewGuid().ToString();
			}

			return data;
		}

		[Test]
		public void TestMessageSerializerException()
		{
			var data = new SnipeObject()
			{
				["value"] = 1000, ["errorCode"] = "ok", ["json"] = "{\"id\":2,\"field\":\"fildvalue\"}",
			};
			var message = new SnipeObject() { ["id"] = 11, ["name"] = "SomeName", ["data"] = data, };

			var serializer = new MessagePackSerializer(4096, true);
			_ = serializer.Serialize(message);

			data["unsupported"] = new CustomUnsupportedData();

			Assert.Catch<MessagePackSerializationUnsupportedTypeException>(() =>
			{
				_ = serializer.Serialize(message);
			});

			serializer = new MessagePackSerializer(4096, false);
			_ = serializer.Serialize(message);
		}

		[Test]
		public void TestMessageSerializerOffset()
		{
			var data = new SnipeObject()
			{
				["value"] = 1000, ["errorCode"] = "ok", ["json"] = "{\"id\":2,\"field\":\"fildvalue\"}",
			};
			var message = new SnipeObject() { ["id"] = 11, ["name"] = "SomeName", ["data"] = data, };

			const int OFFSET = 4;
			var serializer = new MessagePackSerializer(4096);
			var original = serializer.Serialize(message).ToArray();
			var shifted = serializer.Serialize(OFFSET, message).ToArray();

			Assert.AreEqual(OFFSET, shifted.Length - original.Length);
			Assert.AreEqual(original, shifted.AsSpan(OFFSET).ToArray());
		}

		[Test]
		public void TestMessageSerializerDeserialize()
		{
			var data = new SnipeObject()
			{
				["value"] = 1000, ["errorCode"] = "ok", ["json"] = "{\"id\":2,\"field\":\"fildvalue\"}",
			};
			var message = new SnipeObject() { ["id"] = 11, ["name"] = "SomeName", ["data"] = data, };

			var serializer = new MessagePackSerializer(4096);
			var serizlized = serializer.Serialize(message).ToArray();
			var deserialized = MessagePackDeserializer.Parse(serizlized);

			Assert.AreEqual(message, deserialized);
		}

		[Test]
		public void TestSmallBuffer()
		{
			var data = new SnipeObject()
			{
				["value"] = 1000, ["errorCode"] = "ok", ["json"] = "{\"id\":2,\"field\":\"fildvalue\"}",
			};
			var message = new SnipeObject() { ["id"] = 11, ["name"] = "SomeName", ["data"] = data, };

			var serializer = new MessagePackSerializer(4096);
			var serizlized = serializer.Serialize(message).ToArray();

			serializer = new MessagePackSerializer(serizlized.Length / 3);
			var serizlizedNew = serializer.Serialize(message).ToArray();

			Assert.AreEqual(serizlized, serizlizedNew);
		}

		[Test]
		public void TestLargeMapSerialization()
		{
			const int MAP_SIZE = 70000;
			var map = new Dictionary<string, object>(MAP_SIZE);

			for (int i = 0; i < MAP_SIZE; i++)
			{
				map[$"key{i}"] = i;
			}

			var serializer = new MessagePackSerializer(MAP_SIZE * 10);
			var serialized = serializer.Serialize(map).ToArray();

			byte[] expectedHeader = new byte[]
			{
				0xDF,
				(byte)((MAP_SIZE >> 24) & 0xFF),
				(byte)((MAP_SIZE >> 16) & 0xFF),
				(byte)((MAP_SIZE >> 8) & 0xFF),
				(byte)(MAP_SIZE & 0xFF)
			};

			CollectionAssert.AreEqual(expectedHeader, serialized.AsSpan(0, 5).ToArray());
		}

		[Test]
        public void Serialize_ShouldUseInt32FormatForInt32MinValue()
        {
            var map = new Dictionary<string, object>
            {
                ["v"] = Int32.MinValue,
            };

            var serializer = new MessagePackSerializer(64);
            Span<byte> bytes = serializer.Serialize(map);

            byte[] expected = new byte[]
            {
                0x81, // map of 1 element
                0xA1, (byte)'v',
                0xD2, 0x80, 0x00, 0x00, 0x00
            };

            CollectionAssert.AreEqual(expected, bytes.ToArray());
        }

		class CustomUnsupportedData
		{
			public string Value { get; }

			public CustomUnsupportedData()
			{
				Value = Guid.NewGuid().ToString();
			}
		}
	}
}
