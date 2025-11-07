using System;
using MiniIT.Snipe.Configuration;
using MiniIT.Snipe.Unity;

namespace MiniIT.Snipe.Api
{
	public abstract class AbstractSnipeApiContextFactory : ISnipeContextFactory, ISnipeApiContextItemsFactory
	{
		private readonly SnipeConfigBuilder _configBuilder;
		private readonly ISnipeTablesProvider _tablesProvider;

		protected AbstractSnipeApiContextFactory(ISnipeTablesProvider tablesProvider, SnipeConfigBuilder configBuilder)
		{
			_tablesProvider = tablesProvider;
			_configBuilder = configBuilder;
		}

		public SnipeContext CreateContext(int id)
		{
			if (!SnipeServices.IsInitialized)
			{
				SnipeServices.Initialize(new UnitySnipeServicesFactory());
			}

			var config = _configBuilder.Build(id);

			var analytics = SnipeServices.Instance.Analytics.GetTracker(id);
			var communicator = new SnipeCommunicator(analytics);
			var auth = new UnityAuthSubsystem(id, communicator, analytics);
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
			var config = _configBuilder.Build(id);

			context.Communicator.Initialize(config);
			context.Auth.Initialize(config);
			context.Initialize(config);
		}

		public abstract TimeSpan GetServerTimeZoneOffset();
		public abstract AbstractSnipeApiService CreateSnipeApiService(SnipeCommunicator communicator, AuthSubsystem auth);
	}
}
