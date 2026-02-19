using MiniIT.Storage;

namespace MiniIT.Snipe
{
	public sealed class NullSharedPrefs : ISharedPrefs
	{
		public bool HasKey(string key) => false;
		public void DeleteKey(string key) { }
		public void DeleteAll() { }
		public void Save() { }
		public bool GetBool(string key, bool defaultValue = false) => defaultValue;
		public float GetFloat(string key, float defaultValue = 0) => defaultValue;
		public int GetInt(string key, int defaultValue = 0) => defaultValue;
		public string GetString(string key, string defaultValue = null) => defaultValue;
		public void SetBool(string key, bool value) { }
		public void SetFloat(string key, float value) { }
		public void SetInt(string key, int value) { }
		public void SetString(string key, string value) { }
	}
}
