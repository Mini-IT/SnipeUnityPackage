using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe
{
	public partial class SnipeCommunicator : IRoomStateListener
	{
		void IRoomStateListener.OnMatchmakingStarted()
		{
			_logger.LogTrace("OnMatchmakingStarted");

			SetIntensiveHeartbeat(true);
		}

		void IRoomStateListener.OnRoomJoined()
		{
			_logger.LogInformation("OnRoomJoined");

			SetIntensiveHeartbeat(true);
		}

		void IRoomStateListener.OnRoomLeft()
		{
			_logger.LogInformation("OnRoomLeft");

			SetIntensiveHeartbeat(false);
			DisposeRoomRequests();
		}

		private void SetIntensiveHeartbeat(bool value)
		{
			if (_client.GetTransport() is HttpTransport httpTransport)
			{
				httpTransport.IntensiveHeartbeat = value;
			}
		}
	}
}
