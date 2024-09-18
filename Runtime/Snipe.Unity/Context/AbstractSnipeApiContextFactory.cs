using System;
using MiniIT.Snipe.Api;
using MiniIT.Snipe.Unity;

namespace MiniIT.Snipe
{
	public abstract class AbstractSnipeApiContextFactory : ISnipeContextFactory
	{
		public SnipeContext CreateContext(int id)
		{
			if (!SnipeServices.IsInitialized)
			{
				SnipeServices.Initialize(new UnitySnipeServicesFactory());
			}

			var config = new SnipeConfig(id);
			var communicator = new SnipeCommunicator(config);
			var auth = new UnityAuthSubsystem(communicator, config);
			var logReporter = new LogReporter();

			var api = GetApiService(communicator, auth);
			var tables = GetTables();
			var serverTimeZone = TimeZoneInfo.CreateCustomTimeZone("server time", GetServerTimeZoneUtcOffset(), "server time", "server time");
			var context = new SnipeApiContext(id, config, communicator, auth, logReporter, api, tables, serverTimeZone);

			logReporter.SetSnipeContext(context);
			return context;
		}

		protected abstract AbstractSnipeApiService GetApiService(SnipeCommunicator communicator, AuthSubsystem auth);
		protected abstract SnipeApiTables GetTables();
		protected abstract TimeSpan GetServerTimeZoneUtcOffset();
	}
}
