using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT
{
	public partial class SnipeObject : Dictionary<string, object>, IDisposable
	{
		public SnipeObject() { }
		public SnipeObject(IDictionary<string, object> dictionary) : base(dictionary) { }

		// IDisposable
		public void Dispose()
		{
			// copy of keys list for "out of sync" exception workaround
			IEnumerable keys = new List<string>(this.Keys);

			foreach (string key in keys)
			{
				object member = this[key];
				if (member is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}

			Clear();
			GC.SuppressFinalize(this);
		}

		public new object this[string key]
		{
			get
			{
				return TryGetValue(key, out object result) ? result : null;
			}
			set
			{
				base[key] = value;
			}
		}
	}
}
