using System;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe
{
	public abstract class AbstractSnipeApiContext : SnipeContext
	{
		public virtual AbstractSnipeApiService Api { get; protected set; }
	}

	public class SnipeApiContext<TApi, TTables> : AbstractSnipeApiContext
		where TApi : AbstractSnipeApiService
		where TTables : SnipeApiTables, new()
	{
		/// <inheritdoc cref="SnipeContext.Default"/>
		public static new SnipeApiContext<TApi, TTables> Default => GetInstance();

		/// <inheritdoc cref="SnipeContext.GetInstance(string, bool)"/>
		public static new SnipeApiContext<TApi, TTables> GetInstance(string id = null, bool initialize = true)
			=> GetInstance<SnipeApiContext<TApi, TTables>>(id, initialize);
		
		public new TApi Api
		{
			get => _api;
			private set => base.Api = _api = value;
		}

		public TTables Tables { get; private set; }
		public LogicManager LogicManager { get; private set; }
		public BadgesManager BadgesManager { get; private set; }
		public CalendarManager CalendarManager { get; private set; }

		protected TimeZoneInfo _serverTimeZone;

		private TApi _api;

		private TApi CreateApi()
		{
			Type type = typeof(TApi);
			if (type.IsAbstract)
				return null;

			AbstractSnipeApiService.RequestFactoryMethod requestFactory = CreateRequest;

			var constructor = type.GetConstructor(new Type[] { typeof(SnipeCommunicator), typeof(AbstractSnipeApiService.RequestFactoryMethod) });
			return (TApi)constructor.Invoke(new object[] { Communicator, requestFactory });
		}

		private TTables CreateTables()
		{
			var tables = new TTables();

			return tables;
		}

		/// <inheritdoc cref="SnipeContext.SnipeContext"/>
		protected SnipeApiContext() { }

		protected override void Initialize(string id)
		{
			base.Initialize(id);
			
			if (Api != null)
			{
				return;
			}
			
			Communicator.ConnectionSucceeded += OnCommunicatorConnected;

			Api = CreateApi();
			Tables = CreateTables();

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
				CalendarManager = new CalendarManager(calendarTable, _serverTimeZone);
			}
		}

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
			Api = null;
			Tables = null;

			if (Communicator != null)
			{
				Communicator.ConnectionSucceeded -= OnCommunicatorConnected;
			}

			base.Dispose();
		}
	}
}
