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

			var context = InternalCreateContext(id, config, communicator, auth, logReporter);

			logReporter.SetSnipeContext(context);
			return context;
		}

		protected abstract SnipeContext InternalCreateContext(int id, SnipeConfig config, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter);
	}
}
