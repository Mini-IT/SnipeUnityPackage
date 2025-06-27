using MiniIT.Snipe.Configuration;
using MiniIT.Snipe.Unity;

namespace MiniIT.Snipe.Api
{
	public abstract class AbstractSnipeApiContextFactory : ISnipeContextFactory
	{
		private readonly SnipeConfigBuilder _configBuilder;

		protected AbstractSnipeApiContextFactory(SnipeConfigBuilder configBuilder)
		{
			_configBuilder = configBuilder;
		}

		public SnipeContext CreateContext(int id)
		{
			if (!SnipeServices.IsInitialized)
			{
				SnipeServices.Initialize(new UnitySnipeServicesFactory());
			}

			var config = _configBuilder.Build(id);

			var analytics = SnipeServices.Analytics.GetTracker(id);
			var communicator = new SnipeCommunicator(config, analytics);
			var auth = new UnityAuthSubsystem(id, communicator, config, analytics);
			var logReporter = new LogReporter();

			var context = InternalCreateContext(id, communicator, auth, logReporter);
			context.Initialize(config);
			return context;
		}

		protected abstract SnipeContext InternalCreateContext(int id, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter);
	}
}
