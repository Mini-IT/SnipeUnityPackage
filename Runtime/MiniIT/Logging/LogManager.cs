using Microsoft.Extensions.Logging;
using MiniIT.Debug.Logging.Null;

namespace MiniIT.Snipe.Logging
{
	public static class LogManager
	{
		public static ILogger<T> GetLogger<T>() where T : class => GetLoggerFactory().CreateLogger<T>();
		public static ILogger GetLogger(string categoryName) => GetLoggerFactory().CreateLogger(categoryName);

		private static ILoggerFactory s_loggerFactory;

		private static ILoggerFactory GetLoggerFactory()
		{
			s_loggerFactory ??= new NullLoggerFactory();
			return s_loggerFactory;
		}

		public static void Init(ILoggerFactory loggerFactory)
		{
			s_loggerFactory = loggerFactory;
		}
	}

	public static class ILoggerExtension
	{
		public static void Log(this ILogger logger, string message, params object[] args)
		{
			logger.Log(LogLevel.Trace, message, args);
		}
	}
}
