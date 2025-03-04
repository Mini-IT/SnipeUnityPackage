using MiniIT.Snipe.Unity;

namespace MiniIT.Snipe
{
	public interface ISnipeContextFactory
	{
		SnipeContext CreateContext(int id, SnipeConfig config);
	}

	public abstract class AbstractSnipeApiContextFactory : ISnipeContextFactory
	{
		public SnipeContext CreateContext(int id, SnipeConfig config)
		{
			if (!SnipeServices.IsInitialized)
			{
				SnipeServices.Initialize(new UnitySnipeServicesFactory());
			}

			var communicator = new SnipeCommunicator(id, config);
			var auth = new UnityAuthSubsystem(communicator, config);
			var logReporter = new LogReporter();

			var context = InternalCreateContext(id, config, communicator, auth, logReporter);
			return context;
		}

		protected abstract SnipeContext InternalCreateContext(int id, SnipeConfig config, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter);
	}
}
