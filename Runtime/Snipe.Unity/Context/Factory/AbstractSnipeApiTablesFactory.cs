using MiniIT.Snipe.Api;
using MiniIT.Snipe.Configuration;

namespace MiniIT.Snipe
{
	public abstract class AbstractSnipeApiTablesFactory : ISnipeApiTablesFactory
	{
		protected readonly ISnipeServices _services;
		private readonly SnipeOptionsBuilder _optionsBuilder;

		protected AbstractSnipeApiTablesFactory(ISnipeServices services, SnipeOptionsBuilder optionsBuilder)
		{
			_services = services;
			_optionsBuilder = optionsBuilder;
		}

		public TablesOptions TablesOptions { get; } = new TablesOptions();

		public abstract SnipeApiTables CreateSnipeApiTables();

		protected void EnsureDefaultTablesUrls()
		{
			if (TablesOptions.TablesUrls.Count > 0)
			{
				return;
			}

			string projectName = _optionsBuilder.BuildProjectName();
			if (_optionsBuilder.ProjectInfo.Mode == SnipeProjectMode.Dev)
			{
				TablesOptions.AddTableUrl($"https://static-dev.snipe.dev/{projectName}/");
				TablesOptions.AddTableUrl($"https://static-dev-noproxy.snipe.dev/{projectName}/");
			}
			else
			{
				TablesOptions.AddTableUrl($"https://static.snipe.dev/{projectName}/");
				TablesOptions.AddTableUrl($"https://static-noproxy.snipe.dev/{projectName}/");
				TablesOptions.AddTableUrl($"https://snipe.tuner-life.com/{projectName}/");
			}
		}
	}
}
