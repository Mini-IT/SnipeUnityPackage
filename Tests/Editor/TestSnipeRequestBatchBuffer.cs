using System.Collections.Generic;
using NUnit.Framework;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestSnipeRequestBatchBuffer
	{
		[Test]
		public void Add_FlushesWhenMaxBatchSizeReached()
		{
			var buffer = new SnipeRequestBatchBuffer();

			Assert.IsNull(buffer.SetEnabled(true));

			List<IDictionary<string, object>> flushed = null;
			for (int i = 0; i < SnipeClient.MAX_BATCH_SIZE; i++)
			{
				flushed = buffer.Add(CreateMessage(i));
			}

			Assert.IsNotNull(flushed);
			Assert.AreEqual(SnipeClient.MAX_BATCH_SIZE, flushed.Count);
			Assert.IsTrue(buffer.IsEmpty);
		}

		[Test]
		public void SetEnabledFalse_FlushesPendingRequests()
		{
			var buffer = new SnipeRequestBatchBuffer();
			buffer.SetEnabled(true);
			buffer.Add(CreateMessage(1));
			buffer.Add(CreateMessage(2));

			var flushed = buffer.SetEnabled(false);

			Assert.IsFalse(buffer.Enabled);
			Assert.IsNotNull(flushed);
			Assert.AreEqual(2, flushed.Count);
		}

		private static IDictionary<string, object> CreateMessage(int id)
		{
			return new Dictionary<string, object>()
			{
				["t"] = "test",
				["id"] = id,
			};
		}
	}
}
