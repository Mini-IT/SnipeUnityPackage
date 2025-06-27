using System;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe
{
	public class SnipeApiContext : SnipeContext
	{
		public LogicManager LogicManager { get; }
		public GraphLogicManager GraphManager { get; }
		public BadgesManager BadgesManager { get; }
		public CalendarManager CalendarManager { get; }

		private readonly AbstractSnipeApiService _api;

		public SnipeApiContext(int id, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter,
			ISnipeApiContextItemsFactory itemsFactory, ISnipeTablesProvider tablesProvider)
			: base(id, communicator, auth, logReporter)
		{
			_api = itemsFactory.CreateSnipeApiService(communicator, auth);
			var tables = tablesProvider.GetTables();

			var logicTable = tables.GetTable<SnipeTableLogicItem>();
			if (logicTable != null)
			{
				LogicManager = new LogicManager(Communicator, CreateRequest, logicTable);
			}

			var graphsTable = tables.GetTable<SnipeTableGraphsItem>();
			if (graphsTable != null)
			{
				GraphManager = new GraphLogicManager(Communicator, CreateRequest, graphsTable);
			}

			var badgesTable = tables.GetTable<SnipeTableBadgesItem>();
			if (badgesTable != null)
			{
				BadgesManager = new BadgesManager(Communicator, CreateRequest, badgesTable);
			}

			var calendarTable = tables.GetTable<SnipeTableCalendarItem>();
			if (calendarTable != null)
			{
				TimeSpan serverTimeZoneOffset = itemsFactory.GetServerTimeZoneOffset();
				var serverTimeZone = TimeZoneInfo.CreateCustomTimeZone("server time", serverTimeZoneOffset, "server time", "server time");
				CalendarManager = new CalendarManager(calendarTable, serverTimeZone);
			}
		}

		public AbstractSnipeApiService GetSnipeApiService() => _api;

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

			base.Dispose();
		}
	}
}
