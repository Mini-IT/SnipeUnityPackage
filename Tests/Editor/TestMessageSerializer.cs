using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MiniIT;
using MiniIT.Snipe;
using MiniIT.MessagePack;

public class TestMessageSerializer
{
	[Test]
	public void TestWSMessageSerializerMultithread()
	{
		const int THREADS_COUNT = 40;
		List<SnipeObject> data = new List<SnipeObject>(THREADS_COUNT);
		List<byte[]> serialized = new List<byte[]>(data.Count);

		for (int i = 0; i < THREADS_COUNT; i++)
		{
			data.Add(GenerateRandomSnipeObject());
			serialized.Add(MessagePackSerializer.Serialize(data[i]));
		}

		// Unique WebSocketTransport instances 
		List<byte[]> result = Task.Run(async () => await TestWSMessageSerializerAsync(data)).GetAwaiter().GetResult();

		Assert.AreEqual(serialized.Count, result.Count);
		for (int i = 0; i < data.Count; i++)
		{
			Assert.AreEqual(serialized[i], result[i]);
		}

		// Single WebSocketTransport instance
		var transport = new WebSocketTransport();
		result = Task.Run(async () => await TestWSMessageSerializerAsync(data, transport)).GetAwaiter().GetResult();
		Assert.AreEqual(serialized.Count, result.Count);
		for (int i = 0; i < data.Count; i++)
		{
			Assert.AreEqual(serialized[i], result[i]);
		}
	}

	private async Task<List<byte[]>> TestWSMessageSerializerAsync(List<SnipeObject> data, WebSocketTransport transport = null)
	{
		List<byte[]> result = new List<byte[]>(data.Count);
		for (int i = 0; i < data.Count; i++)
		{
			result.Add(null);
		}
		
		List<Task> tasks = new List<Task>(data.Count);

		transport ??= new WebSocketTransport();
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
		for (int i = 0; i < 5; i++)
		{
			int fieldsCount = UnityEngine.Random.Range(1, 5);
			data[$"field{i}"] = i;
			data[Guid.NewGuid().ToString()] = i * 12 + fieldsCount;
			for (int k = 0; k < fieldsCount; k++)
			{
				data[Guid.NewGuid().ToString()] = Guid.NewGuid().ToString();
			}
		}
		return data;
	}

	// A UnityTest behaves like a coroutine in Play Mode. In Edit Mode you can use
	// `yield return null;` to skip a frame.
	//[UnityTest]
	//public IEnumerator TestMessageSerializerWithEnumeratorPasses()
	//{
	//    yield return null;
	//}
}
