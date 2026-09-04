using System.Threading.Tasks;

namespace MiniIT.Snipe.Internal
{
	internal enum SnipeLogBufferCommandType
	{
		Append,
		Rotate,
		Stop
	}

	internal sealed class SnipeLogBufferCommand
	{
		internal SnipeLogBufferCommandType Type { get; }
		internal byte[] Data { get; }
		internal TaskCompletionSource<bool> Completion { get; }
		internal int Offset { get; set; }

		private SnipeLogBufferCommand(
			SnipeLogBufferCommandType type,
			byte[] data,
			TaskCompletionSource<bool> completion)
		{
			Type = type;
			Data = data;
			Completion = completion;
		}

		internal static SnipeLogBufferCommand Append(byte[] data)
		{
			return new SnipeLogBufferCommand(SnipeLogBufferCommandType.Append, data, null);
		}

		internal static SnipeLogBufferCommand Rotate()
		{
			return new SnipeLogBufferCommand(
				SnipeLogBufferCommandType.Rotate,
				null,
				new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
		}

		internal static SnipeLogBufferCommand Stop()
		{
			return new SnipeLogBufferCommand(
				SnipeLogBufferCommandType.Stop,
				null,
				new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
		}
	}
}
