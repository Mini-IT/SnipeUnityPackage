
namespace MiniIT.Snipe.SharedPrefs
{
	public class NullSharedPrefs : ISharedPrefs
	{
		public void DeleteAll() { }
		public void DeleteKey(string key) { }
		public bool GetBool(string key, bool defaultValue = default) => defaultValue;
		public float GetFloat(string key, float defaultValue = default) => defaultValue;
		public int GetInt(string key, int defaultValue = default) => defaultValue;
		public string GetString(string key, string defaultValue = default) => defaultValue;

		public bool HasKey(string key) => false;

		public void Save() { }
		public void SetBool(string key, bool value) { }
		public void SetFloat(string key, float value) { }
		public void SetInt(string key, int value) { }
		public void SetString(string key, string value) { }
	}
}
