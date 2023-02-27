using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Api
{
	public class BadgesManager : IDisposable
	{
		public delegate void GetCallback(string errorCode, List<UserBadge> badges);
		public delegate void BadgesUpdatedHandler(List<UserBadge> badges);
		public delegate void BadgeChangedHandler(UserBadge badge);

		public event BadgesUpdatedHandler BadgesUpdated;
		public event BadgeChangedHandler BadgeChanged;

		public List<UserBadge> _badges = new List<UserBadge>();
		public List<UserBadge> Badges => _badges;

		private readonly AbstractSnipeApiService _snipeApi;
		private readonly LogicManager _logicManager;
		private SnipeCommunicator _snipeCommunicator;

		public BadgesManager(AbstractSnipeApiService snipeApi, LogicManager logicManager)
		{
			_snipeApi = snipeApi;
			_logicManager = logicManager;
			_logicManager.NodeProgress += OnLogicNodeProgress;

			_snipeCommunicator = SnipeCommunicator.Instance;
			_snipeCommunicator.MessageReceived += OnMessageReceived;
		}

		public void Dispose()
		{
			if (_logicManager != null)
			{
				_logicManager.NodeProgress -= OnLogicNodeProgress;
			}
		}

		public bool Get(GetCallback callback = null)
		{
			var request = _snipeApi.CreateRequest("badge.get", null);

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

		private void OnMessageReceived(string messageType, string errorCode, SnipeObject data, int requestId)
		{
			if (errorCode != SnipeErrorCodes.OK)
				return;

			switch (messageType)
			{
				case "badge.get":
					OnBadgesGet(data);
					break;

				case "badge.changed": // badge level changed
					OnBadgeChanged(data);
					break;
			}
		}

		private void OnBadgesGet(SnipeObject data)
		{
			if (data["list"] is IList src_list)
			{
				_badges.Clear();
				foreach (SnipeObject o in src_list)
				{
					_badges.Add(new UserBadge(o));
				}
				BadgesUpdated?.Invoke(_badges);
			}
		}

		private void OnBadgeChanged(SnipeObject data)
		{
			if (data["list"] is IList src_list)
			{
				foreach (SnipeObject o in src_list)
				{
					int badgeId = data.SafeGetValue<int>("id");
					UserBadge badge = _badges.FirstOrDefault(b => b.id == badgeId);
					if (badge != null)
					{
						badge.Update(data);
						BadgeChanged?.Invoke(badge);
					}
				}
			}
		}

		private void OnLogicNodeProgress(LogicNode node, SnipeLogicNodeVar nodeVar, int oldValue)
		{
			SnipeTableLogicRawNodeResult nodeResult = node.results.FirstOrDefault(r => r.type == SnipeTableLogicNodeResult.TYPE_BADGE);
			if (nodeResult == null)
				return;

			UserBadge badge = _badges.FirstOrDefault(b => b.id == nodeResult.itemID);
			if (badge == null)
				return;

			badge.count = nodeVar.value;

			BadgeChanged?.Invoke(badge);
		}
	}
}
