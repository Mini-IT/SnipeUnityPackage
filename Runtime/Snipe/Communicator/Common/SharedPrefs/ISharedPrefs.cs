
namespace MiniIT.Snipe
{
	public interface ISharedPrefs
	{
		bool HasKey(string key);
		void Save();
		void DeleteAll();
		void DeleteKey(string key);
		
		float GetFloat(string key);
		float GetFloat(string key, float defaultValue);
		void SetFloat(string key, float value);
		
		int GetInt(string key);
		int GetInt(string key, int defaultValue);
		void SetInt(string key, int value);
		
		string GetString(string key);
		string GetString(string key, string defaultValue);
		void SetString(string key, string value);
		
		bool GetBool(string key);
		bool GetBool(string key, bool defaultValue);
		void SetBool(string key, bool value);
	}
}