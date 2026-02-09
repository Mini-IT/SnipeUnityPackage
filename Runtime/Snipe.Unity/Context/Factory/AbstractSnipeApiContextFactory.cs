using System;
using MiniIT.Snipe.Configuration;
using MiniIT.Snipe.Unity;
using MiniIT.Snipe;

namespace MiniIT.Snipe.Api
{
	public abstract class AbstractSnipeApiContextFactory : ISnipeContextFactory, ISnipeApiContextItemsFactory
	{
		private readonly SnipeOptionsBuilder _optionsBuilder;
		private readonly ISnipeTablesProvider _tablesProvider;
		protected readonly ISnipeServices _services;
		public TablesOptions TablesOptions { get; } = new TablesOptions();

		protected AbstractSnipeApiContextFactory(
			ISnipeTablesProvider tablesProvider,
			SnipeOptionsBuilder optionsBuilder,
			ISnipeServices services = null)
		{
			_tablesProvider = tablesProvider;
			_optionsBuilder = optionsBuilder;
			_services = services;
		}

		public SnipeContext CreateContext(int id)
		{
			var services = _services ?? SnipeUnityDefaults.CreateDefaultServices();

			var options = _optionsBuilder.Build(id, services);
			InitializeDefaultTablesConfig(options);

			var analytics = (services.Analytics as IAnalyticsTrackerProvider)?.GetTracker(id);
			var communicator = new SnipeCommunicator(options, analytics, services);
			var auth = new UnityAuthSubsystem(id, options, communicator, analytics, services);
			var logReporter = new LogReporter();

			var context = new SnipeApiContext(id, options, communicator, auth, logReporter, this, _tablesProvider);
			return context;
		}

		public void Reconfigure(SnipeContext context)
		{
			int id = context.Id;
			var services = _services ?? SnipeUnityDefaults.CreateDefaultServices();
			var options = _optionsBuilder.Build(id, services);
			InitializeDefaultTablesConfig(options);

			context.Communicator.Reconfigure(options);
			context.Auth.Reconfigure(options);
			context.Reconfigure(options);
		}

		private void InitializeDefaultTablesConfig(SnipeOptions options)
		{
			TablesOptions.ResetTablesUrls();

			if (options.Project.Mode == SnipeProjectMode.Dev)
			{
				TablesOptions.AddTableUrl($"https://static-dev.snipe.dev/{options.ProjectName}/");
				TablesOptions.AddTableUrl($"https://static-dev-noproxy.snipe.dev/{options.ProjectName}/");
			}
			else
			{
				TablesOptions.AddTableUrl($"https://static.snipe.dev/{options.ProjectName}/");
				TablesOptions.AddTableUrl($"https://static-noproxy.snipe.dev/{options.ProjectName}/");
				TablesOptions.AddTableUrl($"https://snipe.tuner-life.com/{options.ProjectName}/");
			}
		}

		public abstract TimeSpan GetServerTimeZoneOffset();
		public abstract AbstractSnipeApiService CreateSnipeApiService(ISnipeCommunicator communicator, AuthSubsystem auth);
	}
}
