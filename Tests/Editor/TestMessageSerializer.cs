using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MiniIT;
using MiniIT.Snipe;
using System;
using MiniIT.MessagePack;

public class TestMessageSerializer
{
	[Test]
	public void TestWSMessageSerializerMultithread()
	{
		List<SnipeObject> data = new List<SnipeObject>(20);
		List<byte[]> serialized = new List<byte[]>(data.Count);

		for (int i = 0; i < data.Count; i++)
		{
			data.Add(GenerateRandomSnipeObject());
			serialized.Add(MessagePackSerializer.Serialize(data[i]));
		}

		List<byte[]> result = Task.Run(async () => await TestWSMessageSerializerAsync(data)).GetAwaiter().GetResult();
		Assert.AreEqual(serialized.Count, result.Count);
		for (int i = 0; i < data.Count; i++)
		{
			Assert.AreEqual(serialized[i], result[i]);
		}
	}

	private async Task<List<byte[]>> TestWSMessageSerializerAsync(List<SnipeObject> data)
	{
		List<byte[]> result = new List<byte[]>(data.Count);
		for (int i = 0; i < data.Count; i++)
		{
			result.Add(null);
		}
		
		List<Task> tasks = new List<Task>(data.Count);

		var transport = new WebSocketTransport();
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
			data[$"field{i}"] = i;
			data[Guid.NewGuid().ToString()] = i * 12;
			data[Guid.NewGuid().ToString()] = Guid.NewGuid().ToString();
			data[Guid.NewGuid().ToString()] = Guid.NewGuid().ToString();
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
