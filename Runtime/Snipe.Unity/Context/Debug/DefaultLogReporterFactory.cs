namespace MiniIT.Snipe
{
	internal sealed class DefaultLogReporterFactory : ILogReporterFactory
	{
		public ILogReporter Create()
		{
			return new LogReporter();
		}
	}
}
