using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using MiniIT.MessagePack;
using MiniIT.Snipe.Configuration;
using MiniIT.Snipe.Unity;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestMessageSerializer
	{
		[Test]
		public void TestWSMessageSerializerMultithread()
		{
			const int THREADS_COUNT = 40;
			List<IDictionary<string, object>> data = new List<IDictionary<string, object>>(THREADS_COUNT);
			List<byte[]> serialized = new List<byte[]>(THREADS_COUNT);

			if (!SnipeServices.IsInitialized)
			{
				SnipeServices.Initialize(new UnitySnipeServicesFactory());
			}

			var serializer = new MessagePackSerializer(4096);
			var config = new SnipeConfig(0, new SnipeConfigData());

			for (int i = 0; i < THREADS_COUNT; i++)
			{
				var obj = GenerateRandomSnipeObject();
				data.Add(obj);
				serialized.Add(serializer.Serialize(obj).ToArray());
			}

			// Unique WebSocketTransport instances
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

		private async Task<List<byte[]>> TestWSMessageSerializerAsync(List<IDictionary<string, object>> data, SnipeConfig config)
		{
			var transport = new WebSocketTransport(config, null);
			return await TestWSMessageSerializerAsync(data, transport);
		}

		private async Task<List<byte[]>> TestWSMessageSerializerAsync(List<IDictionary<string, object>> data,
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

		private IDictionary<string, object> GenerateRandomSnipeObject()
		{
			IDictionary<string, object> data = new Dictionary<string, object>();
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
			var data = new Dictionary<string, object>()
			{
				["value"] = 1000, ["errorCode"] = "ok", ["json"] = "{\"id\":2,\"field\":\"fildvalue\"}",
			};
			var message = new Dictionary<string, object>() { ["id"] = 11, ["name"] = "SomeName", ["data"] = data, };

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
			var data = new Dictionary<string, object>()
			{
				["value"] = 1000, ["errorCode"] = "ok", ["json"] = "{\"id\":2,\"field\":\"fildvalue\"}",
			};
			var message = new Dictionary<string, object>() { ["id"] = 11, ["name"] = "SomeName", ["data"] = data, };

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
			var data = new Dictionary<string, object>()
			{
				["value"] = 1000, ["errorCode"] = "ok", ["json"] = "{\"id\":2,\"field\":\"fildvalue\"}",
			};
			var message = new Dictionary<string, object>() { ["id"] = 11, ["name"] = "SomeName", ["data"] = data, };

			var serializer = new MessagePackSerializer(4096);
			var serizlized = serializer.Serialize(message).ToArray();
			var deserialized = MessagePackDeserializer.Parse(serizlized);

			Assert.AreEqual(message, deserialized);
		}

		[Test]
		public void TestSmallBuffer()
		{
			var data = new Dictionary<string, object>()
			{
				["value"] = 1000, ["errorCode"] = "ok", ["json"] = "{\"id\":2,\"field\":\"fildvalue\"}",
			};
			var message = new Dictionary<string, object>() { ["id"] = 11, ["name"] = "SomeName", ["data"] = data, };

			var serializer = new MessagePackSerializer(4096);
			var serizlized = serializer.Serialize(message).ToArray();

			serializer = new MessagePackSerializer(serizlized.Length / 3);
			var serizlizedNew = serializer.Serialize(message).ToArray();

			Assert.AreEqual(serizlized, serizlizedNew);
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
