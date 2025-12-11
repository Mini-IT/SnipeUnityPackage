using MiniIT.Storage;

namespace MiniIT.Snipe.Api
{
	public class ProfileAttributeFactory
	{
		private readonly ProfileManager _profileManager;
		private readonly ISharedPrefs _sharedPrefs;

		public ProfileAttributeFactory(ProfileManager profileManager, ISharedPrefs sharedPrefs)
		{
			_profileManager = profileManager;
			_sharedPrefs = sharedPrefs;
		}

		public ProfileAttribute<T> CreateAttribute<T>(SnipeApiReadOnlyUserAttribute<T> serverAttribute)
		{
			return _profileManager.GetAttribute(serverAttribute);
		}

		public LocalProfileAttribute<T> CreateLocalAttribute<T>(string key)
		{
			return new LocalProfileAttribute<T>(key, _sharedPrefs);
		}
	}
}
