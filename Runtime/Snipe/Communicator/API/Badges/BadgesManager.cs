using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class BadgesManager : AbstractSnipeApiModuleManagerWithTable
	{
		public delegate void GetCallback(string errorCode, List<UserBadge> badges);
		public delegate void BadgesUpdatedHandler(List<UserBadge> badges);
		public delegate void BadgeChangedHandler(UserBadge badge);

		public event BadgesUpdatedHandler BadgesUpdated;
		public event BadgeChangedHandler BadgeLevel;
		public event BadgeChangedHandler BadgeProgress;
		public List<UserBadge> Badges => _badges;

		public List<UserBadge> _badges = new List<UserBadge>();

		private ISnipeTable<SnipeTableBadgesItem> _badgesTable;
		private GetCallback _getCallback;

		public BadgesManager(ISnipeCommunicator communicator,
			AbstractSnipeApiService.RequestFactoryMethod requestFactory,
			ISnipeTable<SnipeTableBadgesItem> badgesTable)
			: base(communicator, requestFactory, badgesTable)
		{
			_badgesTable = badgesTable;
		}

		~BadgesManager()
		{
			Dispose();
		}

		public override void Dispose()
		{
			base.Dispose();

			_badgesTable = null;

			GC.SuppressFinalize(this);
		}

		public bool Get(GetCallback callback = null)
		{
			var request = _requestFactory.Invoke("badgeV2.get", null);

			if (request == null)
			{
				return false;
			}

			if (callback != null)
			{
				_getCallback += callback;
			}

			request.Request();
			return true;
		}

		public bool TryGetBadge(int badgeId, out UserBadge badge)
		{
			foreach (UserBadge b in _badges)
			{
				if (b.id == badgeId)
				{
					badge = b;
					return true;
				}
			}

			badge = null;
			return false;
		}

		protected override void OnSnipeMessageReceived(string messageType, string errorCode, IDictionary<string, object> data, int requestId)
		{
			if (errorCode != SnipeErrorCodes.OK)
			{
				return;
			}

			switch (messageType)
			{
				case "badgeV2.get":
					ProcessMessage(messageType, errorCode, data, OnBadgesGet);
					break;

				case "badgeV2.progress":
					ProcessMessage(messageType, errorCode, data, OnBadgeProgress);
					break;

				case "badgeV2.level":
					ProcessMessage(messageType, errorCode, data, OnBadgeLevel);
					break;
			}
		}

		private void OnBadgesGet(string errorCode, IDictionary<string, object> data)
		{
			if (data.TryGetValue("list", out object value) && value is IList list)
			{
				_badges.Clear();
				foreach (IDictionary<string, object> o in list)
				{
					_badges.Add(new UserBadge(o));
				}
				BadgesUpdated?.Invoke(_badges);
			}

			if (_getCallback != null)
			{
				_getCallback.Invoke(errorCode, _badges);
				_getCallback = null;
			}
		}

		private void OnBadgeLevel(string errorCode, IDictionary<string, object> data)
		{
			int badgeId = data.SafeGetValue<int>("id");
			int badgeLevel = data.SafeGetValue<int>("level");

			if (TryGetBadge(badgeId, out UserBadge badge))
			{
				badge.Update(data);
				BadgeLevel?.Invoke(badge);
			}
		}

		private void OnBadgeProgress(string errorCode, IDictionary<string, object> data)
		{
			int badgeId = data.SafeGetValue<int>("id");
			int badgeLevel = data.SafeGetValue<int>("level");
			int badgeProgress = data.SafeGetValue<int>("progress");

			if (TryGetBadge(badgeId, out UserBadge badge))
			{
				badge.Update(data);
				BadgeProgress?.Invoke(badge);
			}
		}
	}
}
