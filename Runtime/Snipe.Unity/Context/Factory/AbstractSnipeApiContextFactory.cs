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

			var config = _optionsBuilder.Build(id, services);

			var analytics = (services.Analytics as IAnalyticsTrackerProvider)?.GetTracker(id);
			var communicator = new SnipeCommunicator(analytics, services);
			var auth = new UnityAuthSubsystem(id, communicator, analytics, services);
			var logReporter = new LogReporter();

			communicator.Initialize(config);
			auth.Initialize(config);

			var context = new SnipeApiContext(id, communicator, auth, logReporter, this, _tablesProvider);
			context.Initialize(config);
			return context;
		}

		public void Reconfigure(SnipeContext context)
		{
			int id = context.Id;
			var services = _services ?? SnipeUnityDefaults.CreateDefaultServices();
			var config = _optionsBuilder.Build(id, services);

			context.Communicator.Initialize(config);
			context.Auth.Initialize(config);
			context.Initialize(config);
		}

		public abstract TimeSpan GetServerTimeZoneOffset();
		public abstract AbstractSnipeApiService CreateSnipeApiService(ISnipeCommunicator communicator, AuthSubsystem auth);
	}
}
