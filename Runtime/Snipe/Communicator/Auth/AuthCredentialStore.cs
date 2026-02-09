using MiniIT.Storage;

namespace MiniIT.Snipe
{
	internal sealed class AuthCredentialStore
	{
		private readonly int _contextId;
		private readonly ISharedPrefs _sharedPrefs;
		private readonly IAnalyticsContext _analytics;
		private int _userId;
		private string _authLogin;
		private string _authToken;

		public AuthCredentialStore(int contextId, ISharedPrefs sharedPrefs, IAnalyticsContext analytics)
		{
			_contextId = contextId;
			_sharedPrefs = sharedPrefs;
			_analytics = analytics;
		}

		public int UserID
		{
			get
			{
				if (_userId == 0)
				{
					string key = SnipePrefs.GetLoginUserID(_contextId);
					_userId = _sharedPrefs.GetInt(key, 0);
					if (_userId == 0)
					{
						// Try read a string value for backward compatibility
						string stringValue = _sharedPrefs.GetString(key);
						if (!string.IsNullOrEmpty(stringValue) && int.TryParse(stringValue, out int parsedId))
						{
							_userId = parsedId;
							// resave the value as int
							_sharedPrefs.SetInt(key, _userId);
						}
					}

					if (_userId != 0)
					{
						_analytics.SetUserId(_userId.ToString());
					}
				}
				return _userId;
			}
			set
			{
				_userId = value;
				_sharedPrefs.SetInt(SnipePrefs.GetLoginUserID(_contextId), _userId);

				_analytics.SetUserId(_userId.ToString());
			}
		}

		public bool TryGetAuthData(out string login, out string token)
		{
			login = _authLogin;
			token = _authToken;

			if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(token))
			{
				string authUidKey = SnipePrefs.GetAuthUID(_contextId);
				string authKeyKey = SnipePrefs.GetAuthKey(_contextId);
				_authLogin = login = _sharedPrefs.GetString(authUidKey);
				_authToken = token = _sharedPrefs.GetString(authKeyKey);
			}

			if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(token))
			{
				return false;
			}

			return true;
		}

		public void SetAuthData(string uid, string password)
		{
			_authLogin = uid;
			_authToken = password;

			_sharedPrefs.SetString(SnipePrefs.GetAuthUID(_contextId), uid);
			_sharedPrefs.SetString(SnipePrefs.GetAuthKey(_contextId), password);
			_sharedPrefs.Save();
		}

		public void ClearAuthData()
		{
			_authLogin = null;
			_authToken = null;

			string authUidKey = SnipePrefs.GetAuthUID(_contextId);
			string authKeyKey = SnipePrefs.GetAuthKey(_contextId);
			_sharedPrefs.DeleteKey(authUidKey);
			_sharedPrefs.DeleteKey(authKeyKey);
		}

		public string GetInternalAuthLogin()
		{
			if (string.IsNullOrEmpty(_authLogin))
			{
				_authLogin = _sharedPrefs.GetString(SnipePrefs.GetAuthUID(_contextId));
			}
			return _authLogin;
		}

		public string GetInternalAuthToken()
		{
			if (string.IsNullOrEmpty(_authToken))
			{
				_authToken = _sharedPrefs.GetString(SnipePrefs.GetAuthKey(_contextId));
			}
			return _authToken;
		}
	}
}
