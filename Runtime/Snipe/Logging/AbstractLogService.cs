using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Logging
{
	public abstract class AbstractLogService
	{
		/// <inheritdoc cref="LoggerFactoryExtensions.CreateLogger{T}(ILoggerFactory)" />
		public ILogger<T> GetLogger<T>() where T : class => Factory.CreateLogger<T>();

		/// <inheritdoc cref="ILoggerFactory.CreateLogger(string)" />
		public ILogger GetLogger(string categoryName) => Factory.CreateLogger(categoryName);

		private ILoggerFactory _factory;
		public ILoggerFactory Factory
		{
			get
			{
				_factory ??= CreateLoggerFactory();
				return _factory;
			}

			set
			{
				if (_factory != value)
				{
					_factory?.Dispose();
					_factory = value;
				}
			}
		}

		protected abstract ILoggerFactory CreateLoggerFactory();
	}
}
