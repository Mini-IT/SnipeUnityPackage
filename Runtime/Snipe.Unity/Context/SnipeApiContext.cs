using System;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe
{
	public class SnipeApiContext : SnipeContext
	{
		/// <inheritdoc cref="SnipeContext.Default"/>
		//public static new SnipeApiContext<TApi, TTables> Default => GetInstance();

		/// <inheritdoc cref="SnipeContext.GetInstance(string, bool)"/>
		//public static new SnipeApiContext<TApi, TTables> GetInstance(string id = null, bool initialize = true)
		//	=> InternalGetInstance<SnipeApiContext<TApi, TTables>>(id, initialize);
		
		public AbstractSnipeApiService Api { get; }
		public SnipeApiTables Tables { get; }
		public LogicManager LogicManager { get; }
		public BadgesManager BadgesManager { get; }
		public CalendarManager CalendarManager { get; }

		public SnipeApiContext(string id, SnipeConfig config, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter,
			AbstractSnipeApiService api, SnipeApiTables tables, TimeZoneInfo serverTimeZone)
			: base(id, config, communicator, auth, logReporter)
		{
			Api = api;
			Tables = tables;

			var logicTable = Tables.GetTable<SnipeTableLogicItem>();
			if (logicTable != null)
			{
				LogicManager = new LogicManager(Communicator, CreateRequest, logicTable);
			}

			var badgesTable = Tables.GetTable<SnipeTableBadgesItem>();
			if (badgesTable != null)
			{
				BadgesManager = new BadgesManager(Communicator, CreateRequest, badgesTable);
			}

			var calendarTable = Tables.GetTable<SnipeTableCalendarItem>();
			if (calendarTable != null)
			{
				CalendarManager = new CalendarManager(calendarTable, serverTimeZone);
			}

			Communicator.ConnectionSucceeded += OnCommunicatorConnected;
		}

		//private TApi CreateApi()
		//{
		//	Type type = typeof(TApi);
		//	if (type.IsAbstract)
		//		return null;

		//	AbstractSnipeApiService.RequestFactoryMethod requestFactory = CreateRequest;

		//	var constructor = type.GetConstructor(new Type[] { typeof(SnipeCommunicator), typeof(AbstractSnipeApiService.RequestFactoryMethod) });
		//	return (TApi)constructor.Invoke(new object[] { Communicator, requestFactory });
		//}

		//private TTables CreateTables()
		//{
		//	var tables = new TTables();

		//	return tables;
		//}

		/// <inheritdoc cref="SnipeContext.SnipeContext"/>
		//protected SnipeApiContext() { }

		private void OnCommunicatorConnected()
		{
			if (Tables != null && !Tables.Loaded)
			{
				_ = Tables.Load();
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

			if (Api is IDisposable disposableApi)
			{
				disposableApi.Dispose();
			}
			//Api = null;
			//Tables = null;

			if (Communicator != null)
			{
				Communicator.ConnectionSucceeded -= OnCommunicatorConnected;
			}

			base.Dispose();
		}
	}
}
