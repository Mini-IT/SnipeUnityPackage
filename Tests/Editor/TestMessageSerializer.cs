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

			for (int i = 0; i < THREADS_COUNT; i++)
			{
				var obj = GenerateRandomSnipeObject();
				data.Add(obj);
				serialized.Add(MessagePackSerializer.Serialize(obj));
			}

			// Unique WebSocketTransport instances
			List<byte[]> result = Task.Run(async () => await TestWSMessageSerializerAsync(data)).GetAwaiter()
				.GetResult();

			Assert.AreEqual(serialized.Count, result.Count);
			for (int i = 0; i < data.Count; i++)
			{
				Assert.AreEqual(serialized[i], result[i]);
			}

			// Single WebSocketTransport instance
			var transport = new WebSocketTransport(new SnipeConfig(0), null);
			result = Task.Run(async () => await TestWSMessageSerializerAsync(data, transport)).GetAwaiter().GetResult();
			Assert.AreEqual(serialized.Count, result.Count);
			for (int i = 0; i < data.Count; i++)
			{
				Assert.AreEqual(serialized[i], result[i]);
			}
		}

		private async Task<List<byte[]>> TestWSMessageSerializerAsync(List<SnipeObject> data,
			WebSocketTransport transport = null)
		{
			List<byte[]> result = new List<byte[]>(data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				result.Add(null);
			}

			List<Task> tasks = new List<Task>(data.Count);

			transport ??= new WebSocketTransport(new SnipeConfig(0), null);
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

			_ = MessagePackSerializer.Serialize(message);

			data["unsupported"] = new CustomUnsupportedData();

			Assert.Catch<MessagePackSerializationUnsupportedTypeException>(() =>
			{
				_ = MessagePackSerializer.Serialize(message);
			});

			_ = MessagePackSerializer.Serialize(message, false);
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
			var serializer = new MessagePackSerializerNonAlloc();
			var buffer = new byte[4096];
			var original = serializer.Serialize(ref buffer, message).ToArray();
			var shifted = serializer.Serialize(ref buffer, OFFSET, message).ToArray();

			Assert.AreEqual(OFFSET, shifted.Length - original.Length);
			Assert.AreEqual(original, shifted.AsSpan(OFFSET).ToArray());
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
