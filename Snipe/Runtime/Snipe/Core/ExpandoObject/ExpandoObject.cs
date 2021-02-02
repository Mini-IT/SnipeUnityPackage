// Realization of core functionality of System.Dynamic.ExpandoObject (http://msdn.microsoft.com/en-us/library/system.dynamic.expandoobject.aspx)
//
// Based on
// http://www.amazedsaint.com/2009/09/systemdynamicexpandoobject-similar.html
//
// see also
// http://wiki.unity3d.com/index.php?title=ExpandoObject
// http://stackoverflow.com/questions/1653046/what-are-the-true-benefits-of-expandoobject
// http://www.codeproject.com/Articles/62839/Adventures-with-C-4-0-dynamic-ExpandoObject-Elasti
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Linq;

namespace MiniIT
{
	public partial class ExpandoObject : Dictionary<string, object>, IDisposable // ICloneable
	{
		public ExpandoObject() : base() { }
		public ExpandoObject(IDictionary<string, object> dictionary) : base(dictionary) { }

		// IClonable
		public ExpandoObject Clone()
		//public object Clone()
		{
			/*
			ExpandoObject obj = new ExpandoObject();
			obj.mMembers = new Dictionary <string, object>(mMembers);

			// deep copy all member ExpandoObjects
			IEnumerable keys = new List<string>(obj.GetDynamicMemberNames());  // copy of keys list for "out of sync" exception workaround
			foreach (string key in keys)
			{
				object member = this[key];
				if (member is ExpandoObject)
					obj[key] = (member as ExpandoObject).Clone();
				else if (member is ICloneable)
					obj[key] = (member as ICloneable).Clone();
			}

			return obj;
			*/
			return new ExpandoObject(this);
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
			return "[ExpandoObject]"; // string.Format ("[ExpandoObject]");
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