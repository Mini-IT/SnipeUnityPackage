
namespace MiniIT.Snipe
{
	public static class SharedPrefs
	{
		private static ISharedPrefs _sharedPrefs = new UnitySharedPrefs();

		public static void DeleteAll() => _sharedPrefs.DeleteAll();
		public static void DeleteKey(string key) => _sharedPrefs.DeleteKey(key);
		public static bool GetBool(string key) => _sharedPrefs.GetBool(key);
		public static bool GetBool(string key, bool defaultValue) => _sharedPrefs.GetBool(key, defaultValue);
		public static float GetFloat(string key) => _sharedPrefs.GetFloat(key);
		public static float GetFloat(string key, float defaultValue) => _sharedPrefs.GetFloat(key, defaultValue);
		public static int GetInt(string key) => _sharedPrefs.GetInt(key);
		public static int GetInt(string key, int defaultValue) => _sharedPrefs.GetInt(key, defaultValue);
		public static string GetString(string key) => _sharedPrefs.GetString(key);
		public static string GetString(string key, string defaultValue) => _sharedPrefs.GetString(key, defaultValue);
		public static bool HasKey(string key) => _sharedPrefs.HasKey(key);
		public static void Save() => _sharedPrefs.Save();
		public static void SetBool(string key, bool value) => _sharedPrefs.SetBool(key, value);
		public static void SetFloat(string key, float value) => _sharedPrefs.SetFloat(key, value);
		public static void SetInt(string key, int value) => _sharedPrefs.SetInt(key, value);
		public static void SetString(string key, string value) => _sharedPrefs.SetString(key, value);
	}
}