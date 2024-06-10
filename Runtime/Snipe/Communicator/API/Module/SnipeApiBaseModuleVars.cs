using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
#pragma warning disable IDE0065 // Misplaced using directive
	using GameVarsDictionary = Dictionary<string, SnipeApiGameVar>;
	using GameVarsKeyValuePair = KeyValuePair<string, SnipeApiGameVar>;
#pragma warning restore IDE0065 // Misplaced using directive

	public class SnipeApiBaseModuleVars : SnipeApiModule, IEnumerable<GameVarsKeyValuePair>
	{
		public event Action ValuesInitializationFinished;
		public bool ValuesInitialized { get; private set; } = false;

		protected GameVarsDictionary _items;

		public SnipeApiBaseModuleVars(AbstractSnipeApiService snipeApiService) : base(snipeApiService)
		{
			_items = new GameVarsDictionary();
		}

		protected TValue Register<TValue>(TValue item) where TValue : SnipeApiGameVar
		{
			_items[item.Name] = item;
			return item;
		}

		protected void SetValuesInitialized()
		{
			if (!ValuesInitialized)
			{
				ValuesInitialized = true;
				ValuesInitializationFinished?.Invoke();
			}
		}

		public TValue GetValue<TValue>(string name, TValue defaultValue = default)
		{
			if (_items.TryGetValue(name, out var item) && item is SnipeApiGameVar<TValue> typedItem)
			{
				return typedItem.Value;
			}
			return defaultValue;
		}

		public bool TryGetValue<TValue>(string name, out TValue value)
		{
			if (_items.TryGetValue(name, out var item) && item is SnipeApiGameVar<TValue> typedItem)
			{
				value = typedItem.Value;
				return true;
			}
			value = default;
			return false;
		}

		public GameVarsDictionary.KeyCollection Keys => _items.Keys;
		public GameVarsDictionary.ValueCollection Values => _items.Values;

		public bool ContainsKey(string key) => _items.ContainsKey(key);
		public IEnumerator<GameVarsKeyValuePair> GetEnumerator() => _items.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
	}
}
