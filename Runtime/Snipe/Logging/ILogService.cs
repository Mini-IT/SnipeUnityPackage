using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Logging
{
	public interface ILogService
	{
		/// <inheritdoc cref="LoggerFactoryExtensions.CreateLogger{T}(ILoggerFactory)" />
		ILogger<T> GetLogger<T>() where T : class;

		/// <inheritdoc cref="ILoggerFactory.CreateLogger(string)" />
		ILogger GetLogger(string categoryName);
	}
}
