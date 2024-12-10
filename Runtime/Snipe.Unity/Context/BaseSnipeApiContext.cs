using System;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe
{
	public class BaseSnipeApiContext : SnipeContext
	{
		public LogicManager LogicManager { get; }
		public GraphLogicManager GraphManager { get; }
		public BadgesManager BadgesManager { get; }
		public CalendarManager CalendarManager { get; }

		protected AbstractSnipeApiService _api;
		protected SnipeApiTables _tables;

		public BaseSnipeApiContext(int id, SnipeConfig config, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter,
			AbstractSnipeApiService api, SnipeApiTables tables, TimeZoneInfo serverTimeZone)
			: base(id, config, communicator, auth, logReporter)
		{
			_api = api;
			_tables = tables;

			var logicTable = tables.GetTable<SnipeTableLogicItem>();
			if (logicTable != null)
			{
				LogicManager = new LogicManager(Communicator, CreateRequest, logicTable);
			}

			GraphManager = new GraphLogicManager(Communicator, CreateRequest);

			var badgesTable = tables.GetTable<SnipeTableBadgesItem>();
			if (badgesTable != null)
			{
				BadgesManager = new BadgesManager(Communicator, CreateRequest, badgesTable);
			}

			var calendarTable = tables.GetTable<SnipeTableCalendarItem>();
			if (calendarTable != null)
			{
				CalendarManager = new CalendarManager(calendarTable, serverTimeZone);
			}

			Communicator.ConnectionSucceeded += OnCommunicatorConnected;
		}

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
				Communicator.ConnectionSucceeded -= OnCommunicatorConnected;
			}

			base.Dispose();
		}
	}
}
