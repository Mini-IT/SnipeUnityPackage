using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Linq;

namespace MiniIT
{
	public partial class SnipeObject : Dictionary<string, object>, IDisposable // ICloneable
	{
		public SnipeObject() : base() { }
		public SnipeObject(IDictionary<string, object> dictionary) : base(dictionary) { }

		// IClonable
		public SnipeObject Clone()
		//public object Clone()
		{
			/*
			SnipeObject obj = new SnipeObject();
			obj.mMembers = new Dictionary <string, object>(mMembers);

			// deep copy all member SnipeObjects
			IEnumerable keys = new List<string>(obj.GetDynamicMemberNames());  // copy of keys list for "out of sync" exception workaround
			foreach (string key in keys)
			{
				object member = this[key];
				if (member is SnipeObject)
					obj[key] = (member as SnipeObject).Clone();
				else if (member is ICloneable)
					obj[key] = (member as ICloneable).Clone();
			}

			return obj;
			*/
			return new SnipeObject(this);
		}

		// IDisposable
		public void Dispose()
		{
			IEnumerable keys = new List<string>(this.Keys);  // copy of keys list for "out of sync" exception workaround
			foreach (string key in keys)
			{
				object member = this[key];
				if (member is IDisposable)
					(member as IDisposable).Dispose();
			}

			Clear();
			GC.SuppressFinalize(this);
		}
		
		public bool TryGetValue<T>(string field_name, ref T result)
		{
			object res;
			if (TryGetValue(field_name, out res))
			{
				try
				{
					result = (T)res;
				}
				catch (InvalidCastException)
				{
					try
					{
						result = (T)Convert.ChangeType(res, typeof(T));
					}
					catch (Exception)
					{
						return false;
					}
				}
				catch (NullReferenceException) // field exists but res is null
				{
					return false;
				}

				return true;
			}
			else
			{
				return false;
			}
		}

		public T SafeGetValue<T>(string key, T default_value = default)
		{
			T result = default_value;
			this.TryGetValue<T>((string)key, ref result);
			return result;
		}

		public string SafeGetString(string key, string default_value = "")
		{
			object value;
			if (this.TryGetValue(key, out value))
				return Convert.ToString(value);
			return default_value;
		}

		public new object this[string key]
		{
			get
			{
				object result;
				if (base.TryGetValue(key, out result))
					return result;
				return null;
			}
			set
			{
				base[key] = value;
			}
		}
		/*
		public override string ToString ()
		{
			return "[SnipeObject]";
		}
		*/
		
		public static bool ContentEquals(Dictionary<string, object> first, Dictionary<string, object> second)
        {
			// based on https://stackoverflow.com/a/31590664
			
			if (first == second)
				return true;
			if (first == null || second == null)
				return false;
			if (first.Count == second.Count)
				return false;
			
            return second.OrderBy(kvp => kvp.Key).SequenceEqual(first.OrderBy(kvp => kvp.Key));
        }
	}
}