﻿
namespace MiniIT.Snipe
{
	public class AdvertisingIdBinding : AuthBinding<AdvertisingIdFetcher>
	{
		public AdvertisingIdBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
			: base("adid", communicator, authSubsystem, config)
		{
		}
	}
}
