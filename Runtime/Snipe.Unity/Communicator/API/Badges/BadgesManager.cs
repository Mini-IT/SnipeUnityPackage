using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

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

		private SnipeTable<SnipeTableBadgesItem> _badgesTable;

		public BadgesManager(SnipeCommunicator communicator,
			AbstractSnipeApiService.RequestFactoryMethod requestFactory,
			SnipeTable<SnipeTableBadgesItem> badgesTable)
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
				return false;

			SnipeCommunicatorRequest.ResponseHandler responseHandler = null;
			if (callback == null)
			{
				responseHandler = async (error_code, response_data) =>
				{
					// The _badges list is updated in OnBadgesGet method.
					// So just let it do its work
					await Task.Yield();

					callback?.Invoke(error_code, _badges);
				};
			}

			request.Request(responseHandler);
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

		protected override void OnSnipeMessageReceived(string messageType, string errorCode, SnipeObject data, int requestId)
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

		private void OnBadgesGet(string errorCode, SnipeObject data)
		{
			if (data["list"] is IList list)
			{
				_badges.Clear();
				foreach (SnipeObject o in list)
				{
					_badges.Add(new UserBadge(o));
				}
				BadgesUpdated?.Invoke(_badges);
			}
		}

		private void OnBadgeLevel(string errorCode, SnipeObject data)
		{
			int badgeId = data.SafeGetValue<int>("id");
			int badgeLevel = data.SafeGetValue<int>("level");

			if (TryGetBadge(badgeId, out UserBadge badge))
			{
				badge.Update(data);
				BadgeLevel?.Invoke(badge);
			}
		}

		private void OnBadgeProgress(string errorCode, SnipeObject data)
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
