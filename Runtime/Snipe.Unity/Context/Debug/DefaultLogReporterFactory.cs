namespace MiniIT.Snipe
{
	public sealed class DefaultLogReporterFactory : ILogReporterFactory
	{
		public ILogReporter Create()
		{
			return new LogReporter();
		}
	}
}
