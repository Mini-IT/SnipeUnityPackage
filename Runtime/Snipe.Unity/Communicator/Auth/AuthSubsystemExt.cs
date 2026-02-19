namespace MiniIT.Snipe.Unity
{
	public static class AuthSubsystemExt
	{
		public static void RegisterDefaultBindings(this AuthSubsystem auth)
		{
			if (!auth.TryGetBinding<DeviceIdBinding>(false, out _))
			{
				auth.RegisterBinding(new DeviceIdBinding(auth.Services));
			}

			if (!auth.TryGetBinding<AdvertisingIdBinding>(false, out _))
			{
				auth.RegisterBinding(new AdvertisingIdBinding(auth.Services));
			}

#if SNIPE_FACEBOOK
			if (!auth.TryGetBinding<FacebookBinding>(false, out _))
			{
				auth.RegisterBinding(new FacebookBinding(auth.Services));
			}
#endif

#if UNITY_ANDROID
			if (!auth.TryGetBinding<AmazonBinding>(false, out _))
			{
				auth.RegisterBinding(new AmazonBinding(auth.Services));
			}
#endif
		}
	}
}
