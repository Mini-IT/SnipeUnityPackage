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

			var communicator = new SnipeCommunicator(id, config);
			var auth = new UnityAuthSubsystem(communicator, config);
			var logReporter = new LogReporter();

			var context = InternalCreateContext(id, config, communicator, auth, logReporter);
			return context;
		}

		protected abstract SnipeContext InternalCreateContext(int id, SnipeConfig config, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter);
	}
}
