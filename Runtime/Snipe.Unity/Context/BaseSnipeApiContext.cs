using System;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe
{
	/* Usage example */
	/*
	public class SnipeApiContextFactory : AbstractSnipeApiContextFactory, ISnipeApiContextItemsFactory
	{
		protected override SnipeContext InternalCreateContext(int id, SnipeConfig config, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter)
		{
			return new SnipeApiContext(id, config, communicator, auth, logReporter, this);
		}

		public TimeSpan GetServerTimeZoneOffset() => TimeSpan.FromHours(3);
		public SnipeApiTables CreateSnipeApiTables() => new SnipeTables();
		public AbstractSnipeApiService CreateSnipeApiService(SnipeCommunicator communicator, AuthSubsystem auth) => new SnipeApiService(communicator, auth);
	}

	public static class SnipeApiContextExtension
	{
		public static SnipeApiService GetApi(this SnipeApiContext context) => (SnipeApiService)context.GetSnipeApiService();
		public static SnipeTables GetTables(this SnipeApiContext context) => (SnipeTables)context.GetSnipeApiTables();
	}
	*/

	public interface ISnipeApiContextItemsFactory
	{
		TimeSpan GetServerTimeZoneOffset();
		SnipeApiTables CreateSnipeApiTables();
		AbstractSnipeApiService CreateSnipeApiService(SnipeCommunicator communicator, AuthSubsystem auth);
	}

	public class SnipeApiContext : SnipeContext
	{
		public LogicManager LogicManager { get; }
		public GraphLogicManager GraphManager { get; }
		public BadgesManager BadgesManager { get; }
		public CalendarManager CalendarManager { get; }

		private readonly AbstractSnipeApiService _api;
		private readonly SnipeApiTables _tables;

		public SnipeApiContext(int id, SnipeConfig config, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter,
			ISnipeApiContextItemsFactory itemsFactory)
			: base(id, config, communicator, auth, logReporter)
		{
			_api = itemsFactory.CreateSnipeApiService(communicator, auth);
			_tables = itemsFactory.CreateSnipeApiTables();

			var logicTable = _tables.GetTable<SnipeTableLogicItem>();
			if (logicTable != null)
			{
				LogicManager = new LogicManager(Communicator, CreateRequest, logicTable);
			}

			var graphsTable = _tables.GetTable<SnipeTableGraphsItem>();
			if (graphsTable != null)
			{
				GraphManager = new GraphLogicManager(Communicator, CreateRequest, graphsTable);
			}

			var badgesTable = _tables.GetTable<SnipeTableBadgesItem>();
			if (badgesTable != null)
			{
				BadgesManager = new BadgesManager(Communicator, CreateRequest, badgesTable);
			}

			var calendarTable = _tables.GetTable<SnipeTableCalendarItem>();
			if (calendarTable != null)
			{
				TimeSpan serverTimeZoneOffset = itemsFactory.GetServerTimeZoneOffset();
				var serverTimeZone = TimeZoneInfo.CreateCustomTimeZone("server time", serverTimeZoneOffset, "server time", "server time");
				CalendarManager = new CalendarManager(calendarTable, serverTimeZone);
			}

			Communicator.ConnectionEstablished += OnCommunicatorConnected;
		}

		public AbstractSnipeApiService GetSnipeApiService() => _api;
		public SnipeApiTables GetSnipeApiTables() => _tables;

		private void OnCommunicatorConnected()
		{
			if (_tables != null && !_tables.Loaded)
			{
				_ = _tables.Load();
			}
		}

		public override void Dispose()
		{
			if (IsDisposed)
			{
				return;
			}

			LogicManager?.Dispose();
			CalendarManager?.Dispose();
			BadgesManager?.Dispose();

			if (_api is IDisposable disposableApi)
			{
				disposableApi.Dispose();
			}

			if (_tables is IDisposable disposableTables)
			{
				disposableTables.Dispose();
			}

			if (Communicator != null)
			{
				Communicator.ConnectionEstablished -= OnCommunicatorConnected;
			}

			base.Dispose();
		}
	}
}
