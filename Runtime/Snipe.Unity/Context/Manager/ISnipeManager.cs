namespace MiniIT.Snipe
{
	public interface ISnipeManager : ISnipeContextProvider, ISnipeTablesProvider
	{
		bool Initialized { get; }
		ISnipeServices Services { get; }
		TablesOptions TablesOptions { get; }

		void Initialize(ISnipeContextFactory contextFactory, ISnipeApiTablesFactory tablesFactory);
	}
}
