using System.Threading.Tasks;

namespace MiniIT.Snipe.Internal
{
	internal enum SnipeLogBufferCommandType
	{
		Append,
		SealCurrentFile,
		Stop
	}

	internal sealed class SnipeLogBufferCommand
	{
		internal SnipeLogBufferCommandType CommandType { get; }
		internal byte[] Data { get; }
		internal TaskCompletionSource<SnipeLogCacheDirectory.SendBatch> Completion { get; }
		internal int Offset { get; set; }

		private SnipeLogBufferCommand(
			SnipeLogBufferCommandType commandType,
			byte[] data,
			TaskCompletionSource<SnipeLogCacheDirectory.SendBatch> completion)
		{
			CommandType = commandType;
			Data = data;
			Completion = completion;
		}

		internal static SnipeLogBufferCommand Append(byte[] data)
		{
			return new SnipeLogBufferCommand(SnipeLogBufferCommandType.Append, data, null);
		}

		internal static SnipeLogBufferCommand SealCurrentFile()
		{
			return new SnipeLogBufferCommand(
				SnipeLogBufferCommandType.SealCurrentFile,
				null,
				new TaskCompletionSource<SnipeLogCacheDirectory.SendBatch>(TaskCreationOptions.RunContinuationsAsynchronously));
		}

		internal static SnipeLogBufferCommand Stop()
		{
			return new SnipeLogBufferCommand(
				SnipeLogBufferCommandType.Stop,
				null,
				null);
		}
	}
}
