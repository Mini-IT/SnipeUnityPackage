
namespace MiniIT.Snipe
{
	public interface ISharedPrefs
	{
		bool HasKey(string key);
		void DeleteKey(string key);
		void DeleteAll();
		void Save();

		float GetFloat(string key, float defaultValue = default);
		void SetFloat(string key, float value);
		
		int GetInt(string key, int defaultValue = default);
		void SetInt(string key, int value);
		
		string GetString(string key, string defaultValue = default);
		void SetString(string key, string value);
		
		bool GetBool(string key, bool defaultValue = default);
		void SetBool(string key, bool value);
	}
}
