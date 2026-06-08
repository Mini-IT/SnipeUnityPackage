using System;
using System.Collections.Generic;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	internal sealed class SnipePendingSend
	{
		public IDictionary<string, object> Message;
		public List<IDictionary<string, object>> Batch;
		public bool AutoBatchAllowed;

		public int RequestCount => Batch?.Count ?? 1;
		public bool IsBatch => Batch != null;
		public bool CanJoinAutoBatch => Batch == null && AutoBatchAllowed && CanAutoBatch(Message);

		public static SnipePendingSend CreateMessage(IDictionary<string, object> message, bool autoBatchAllowed)
		{
			return new SnipePendingSend()
			{
				Message = message,
				AutoBatchAllowed = autoBatchAllowed,
			};
		}

		public static SnipePendingSend CreateBatch(List<IDictionary<string, object>> batch)
		{
			return new SnipePendingSend()
			{
				Batch = batch,
			};
		}

		public SnipePendingSend SplitBatch(int count)
		{
			var batch = new List<IDictionary<string, object>>(count);
			var remainder = new List<IDictionary<string, object>>(Batch.Count - count);

			for (int i = 0; i < Batch.Count; i++)
			{
				if (i < count)
				{
					batch.Add(Batch[i]);
				}
				else
				{
					remainder.Add(Batch[i]);
				}
			}

			Batch = remainder;

			return CreateBatch(batch);
		}

		public bool TrySend(Func<IDictionary<string, object>, bool> sendRequest, Func<List<IDictionary<string, object>>, bool> sendBatch)
		{
			return IsBatch ? sendBatch(Batch) : sendRequest(Message);
		}

		public void AddDroppedRequests(ref List<IDictionary<string, object>> droppedRequests)
		{
			if (IsBatch)
			{
				for (int i = 0; i < Batch.Count; i++)
				{
					AddDroppedRequest(ref droppedRequests, Batch[i]);
				}
			}
			else
			{
				AddDroppedRequest(ref droppedRequests, Message);
			}
		}

		private static bool CanAutoBatch(IDictionary<string, object> message)
		{
			return message != null && SnipeRequestMessageSizeEsimator.EstimateSizeSmall(message);
		}

		private static void AddDroppedRequest(ref List<IDictionary<string, object>> droppedRequests, IDictionary<string, object> message)
		{
			if (message == null)
			{
				return;
			}

			int requestId = message.SafeGetValue<int>("id");
			if (requestId != 0 && droppedRequests != null)
			{
				for (int i = 0; i < droppedRequests.Count; i++)
				{
					if (droppedRequests[i].SafeGetValue<int>("id") == requestId)
					{
						return;
					}
				}
			}

			droppedRequests ??= new List<IDictionary<string, object>>();
			droppedRequests.Add(message);
		}
	}
}
