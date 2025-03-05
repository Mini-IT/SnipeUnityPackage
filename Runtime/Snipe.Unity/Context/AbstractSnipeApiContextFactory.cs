using MiniIT.Snipe.Configuration;
using MiniIT.Snipe.Unity;

namespace MiniIT.Snipe
{
	public interface ISnipeContextFactory
	{
		SnipeContext CreateContext(int id, SnipeConfigBuilder config);
	}

	public abstract class AbstractSnipeApiContextFactory : ISnipeContextFactory
	{
		public SnipeContext CreateContext(int id, SnipeConfigBuilder configBuilder)
		{
			if (!SnipeServices.IsInitialized)
			{
				SnipeServices.Initialize(new UnitySnipeServicesFactory());
			}

			var config = configBuilder.Build(id);

			var communicator = new SnipeCommunicator(id, config);
			var auth = new UnityAuthSubsystem(communicator, config);
			var logReporter = new LogReporter();

			var context = InternalCreateContext(id, config, communicator, auth, logReporter);
			return context;
		}

		protected abstract SnipeContext InternalCreateContext(int id, SnipeConfig config, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter);
	}
}
