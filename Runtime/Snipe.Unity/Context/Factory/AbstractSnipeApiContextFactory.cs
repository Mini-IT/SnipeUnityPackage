using System;
using MiniIT.Snipe.Configuration;
using MiniIT.Snipe.Unity;

namespace MiniIT.Snipe.Api
{
	public abstract class AbstractSnipeApiContextFactory : ISnipeContextFactory, ISnipeApiContextItemsFactory
	{
		private readonly object _logReporterFactoryLock = new object();
		private readonly SnipeOptionsBuilder _optionsBuilder;
		private readonly ISnipeTablesProvider _tablesProvider;
		private readonly ISnipeServices _services;
		private ILogReporterFactory _logReporterFactory = new DefaultLogReporterFactory();
		private bool _logReporterFactoryLocked;
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

		public void SetLogReporterFactory(ILogReporterFactory logReporterFactory)
		{
			if (logReporterFactory == null)
			{
				throw new ArgumentNullException(nameof(logReporterFactory));
			}

			lock (_logReporterFactoryLock)
			{
				if (_logReporterFactoryLocked)
				{
					throw new InvalidOperationException("Log reporter factory cannot be changed after a reporter has been created.");
				}

				_logReporterFactory = logReporterFactory;
			}
		}

		public SnipeContext CreateContext(int id)
		{
			var options = _optionsBuilder.Build(id, _services);

			var analytics = (_services.Analytics as IAnalyticsTrackerProvider)?.GetTracker(id);
			var communicator = new SnipeCommunicator(options, analytics, _services);
			var auth = new UnityAuthSubsystem(id, options, communicator, analytics, _services);
			var logReporter = CreateLogReporter();

			var context = new SnipeApiContext(id, options, communicator, auth, logReporter, this, _tablesProvider);
			return context;
		}

		protected ILogReporter CreateLogReporter()
		{
			ILogReporterFactory logReporterFactory;
			lock (_logReporterFactoryLock)
			{
				_logReporterFactoryLocked = true;
				logReporterFactory = _logReporterFactory;
			}

			ILogReporter logReporter = logReporterFactory.Create();
			if (logReporter == null)
			{
				throw new InvalidOperationException($"{nameof(ILogReporterFactory)} returned null.");
			}

			return logReporter;
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
