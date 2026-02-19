using MiniIT.Snipe.Api;

namespace MiniIT.Snipe
{
	public interface ISnipeApiTablesFactory
	{
		TablesOptions TablesOptions { get; }
		SnipeApiTables CreateSnipeApiTables();
	}
}
