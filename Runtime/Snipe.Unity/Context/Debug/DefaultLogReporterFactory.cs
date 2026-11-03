namespace MiniIT.Snipe
{
	internal sealed class DefaultLogReporterFactory : ILogReporterFactory
	{
		public ILogReporter CreateLogReporter()
		{
			return new LogReporter();
		}
	}
}
