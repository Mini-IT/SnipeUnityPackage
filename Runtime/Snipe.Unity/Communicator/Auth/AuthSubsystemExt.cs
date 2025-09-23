namespace MiniIT.Snipe.Unity
{
	public static class AuthSubsystemExt
	{
		public static void RegisterDefaultBindings(this AuthSubsystem auth)
		{
			if (!auth.TryGetBinding<DeviceIdBinding>(false, out _))
			{
				auth.RegisterBinding(new DeviceIdBinding());
			}

			if (!auth.TryGetBinding<AdvertisingIdBinding>(false, out _))
			{
				auth.RegisterBinding(new AdvertisingIdBinding());
			}

#if SNIPE_FACEBOOK
			if (!auth.TryGetBinding<FacebookBinding>(false, out _))
			{
				auth.RegisterBinding(new FacebookBinding());
			}
#endif

#if UNITY_ANDROID
			if (!auth.TryGetBinding<AmazonBinding>(false, out _))
			{
				auth.RegisterBinding(new AmazonBinding());
			}
#endif
		}
	}
}
