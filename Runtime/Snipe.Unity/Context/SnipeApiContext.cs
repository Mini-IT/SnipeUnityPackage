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
		public CalendarV2Manager CalendarV2Manager { get; }

		private readonly AbstractSnipeApiService _api;

		public SnipeApiContext(int id, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter,
			ISnipeApiContextItemsFactory itemsFactory, ISnipeTablesProvider tablesProvider)
			: base(id, communicator, auth, logReporter)
		{
			_api = itemsFactory.CreateSnipeApiService(communicator, auth);
			var tables = tablesProvider.GetSnipeTables();

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
			var calendarV2Table = tables.GetTable<SnipeTableCalendarV2Item>();

			if (calendarTable != null || calendarV2Table != null)
			{
				TimeSpan serverTimeZoneOffset = itemsFactory.GetServerTimeZoneOffset();
				var serverTimeZone = TimeZoneInfo.CreateCustomTimeZone("server time", serverTimeZoneOffset, "server time", "server time");

				if (calendarTable != null)
				{
					CalendarManager = new CalendarManager(calendarTable, serverTimeZone);
				}

				if (calendarV2Table != null)
				{
					CalendarV2Manager = new CalendarV2Manager(calendarV2Table, serverTimeZone);
				}
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
			CalendarV2Manager?.Dispose();

			if (_api is IDisposable disposableApi)
			{
				disposableApi.Dispose();
			}

			base.Dispose();
		}
	}
}
