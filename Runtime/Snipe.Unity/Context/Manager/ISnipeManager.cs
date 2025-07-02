namespace MiniIT.Snipe
{
	public interface ISnipeManager : ISnipeContextProvider, ISnipeTablesProvider
	{
		bool Initialized { get; }
		void Initialize(ISnipeContextFactory contextFactory, ISnipeApiTablesFactory tablesFactory);
	}
}
