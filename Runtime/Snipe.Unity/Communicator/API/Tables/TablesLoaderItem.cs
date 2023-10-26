using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class TablesLoaderItem
	{
		public SnipeTable Table => _table;
		public string Name => _name;
		public Type WrapperType => _wrapperType;

		private readonly SnipeTable _table;
		private readonly string _name;
		private readonly Type _wrapperType;

		public TablesLoaderItem(Type wrapperType, SnipeTable table, string name)
		{
			_table = table;
			_name = name;
			_wrapperType = wrapperType;
		}

		public override bool Equals(object obj) => obj is TablesLoaderItem item && EqualityComparer<SnipeTable>.Default.Equals(_table, item._table) && _name == item._name && EqualityComparer<Type>.Default.Equals(_wrapperType, item._wrapperType);

		public override int GetHashCode()
		{
			int hashCode = 633863528;
			hashCode = hashCode * -1521134295 + EqualityComparer<SnipeTable>.Default.GetHashCode(_table);
			hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(_name);
			hashCode = hashCode * -1521134295 + EqualityComparer<Type>.Default.GetHashCode(_wrapperType);
			return hashCode;
		}
	}
}
