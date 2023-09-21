using Microsoft.Extensions.Logging;
using MiniIT.Logging.Unity;

namespace MiniIT.Snipe.Logging
{
	public static class LogManager
	{
		/// <inheritdoc cref="LoggerFactoryExtensions.CreateLogger{T}(ILoggerFactory)" />
		public static ILogger<T> GetLogger<T>() where T : class => Factory.CreateLogger<T>();

		/// <inheritdoc cref="ILoggerFactory.CreateLogger(string)" />
		public static ILogger GetLogger(string categoryName) => Factory.CreateLogger(categoryName);

		private static ILoggerFactory s_factory;
		public static ILoggerFactory Factory
		{
			get
			{
				s_factory ??= new UnityLoggerFactory();
				return s_factory;
			}

			set
			{
				if (s_factory != value)
				{
					s_factory?.Dispose();
					s_factory = value;
				}
			}
		}
	}
}
