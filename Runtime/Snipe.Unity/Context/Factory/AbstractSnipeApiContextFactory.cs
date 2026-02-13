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
		private readonly ISnipeServices _services;
		public TablesOptions TablesOptions { get; } = new TablesOptions();

		protected AbstractSnipeApiContextFactory(
			ISnipeTablesProvider tablesProvider,
			SnipeOptionsBuilder optionsBuilder,
			ISnipeServices services)
		{
			_tablesProvider = tablesProvider;
			_optionsBuilder = optionsBuilder;
			_services = services;
		}

		public SnipeContext CreateContext(int id)
		{
			var options = _optionsBuilder.Build(id, _services);

			var analytics = (_services.Analytics as IAnalyticsTrackerProvider)?.GetTracker(id);
			var communicator = new SnipeCommunicator(options, analytics, _services);
			var auth = new UnityAuthSubsystem(id, options, communicator, analytics, _services);
			var logReporter = new LogReporter();

			var context = new SnipeApiContext(id, options, communicator, auth, logReporter, this, _tablesProvider);
			return context;
		}

		public void Reconfigure(SnipeContext context)
		{
			int id = context.Id;
			var options = _optionsBuilder.Build(id, _services);

			context.Communicator.Reconfigure(options);
			context.Auth.Reconfigure(options);
			context.Reconfigure(options);
		}

		public abstract TimeSpan GetServerTimeZoneOffset();
		public abstract AbstractSnipeApiService CreateSnipeApiService(ISnipeCommunicator communicator, AuthSubsystem auth);
	}
}
