namespace MiniIT.Snipe
{
	public static class SystemInformationExtractor
	{
		public static ISystemInformationExtractor Instance
		{
			get
			{
#if UNITY_WSA && !UNITY_EDITOR
				return new WindowsSystemInfoExtractor();
#elif UNITY_IOS && !UNITY_EDITOR
				return new IosSystemInfoExtractor();
#else
				return new DefaultSystemInfoExtractor();
#endif
			}
		}
	}
}
