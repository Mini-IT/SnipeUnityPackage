using System;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe
{
	public interface ISnipeApiContextItemsFactory
	{
		TimeSpan GetServerTimeZoneOffset();
		AbstractSnipeApiService CreateSnipeApiService(ISnipeCommunicator communicator, AuthSubsystem auth);
	}
}
