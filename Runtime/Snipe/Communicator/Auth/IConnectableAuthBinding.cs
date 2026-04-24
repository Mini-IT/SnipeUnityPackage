using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe
{
	public interface IConnectableAuthBinding
	{
		void Connect(AuthBinding anotherBinding, BindResultCallback callback = null);
	}

	public static class ConnectableAuthBindingExtensions
	{
		public static void Connect<TBinding>(this IConnectableAuthBinding connectable, BindResultCallback callback = null) where TBinding : AuthBinding
		{
			if (connectable is not AuthBinding connectableBinding)
			{
				return;
			}

			var auth = connectableBinding.GetAuthSubsystem();
			if (auth.TryGetBinding<TBinding>(out var binding))
			{
				connectable.Connect(binding, callback);
			}
		}
	}

	public abstract class ConnectableAuthBinding<TFetcher> : AuthBinding<TFetcher>, IConnectableAuthBinding where TFetcher : AuthIdFetcher, new()
	{
		protected ConnectableAuthBinding(string providerId, ISnipeServices services)
			: base(providerId, services)
		{
		}

		public void Connect(AuthBinding anotherBinding, BindResultCallback callback = null)
		{
			if (string.Equals(ProviderId, anotherBinding.ProviderId, StringComparison.OrdinalIgnoreCase))
			{
				// It is not possible to connect to the same provider

				_logger.LogTrace("Failed connecting bindings. It is not possible to connect to the same provider.");

				callback?.Invoke(this, SnipeErrorCodes.PARAMS_WRONG);
				return;
			}

			string pass = GetAuthToken();
			if (string.IsNullOrEmpty(pass))
			{
				_logger.LogTrace("Failed connecting bindings. Auth token is empty.");

				callback?.Invoke(this, SnipeErrorCodes.WRONG_TOKEN);
				return;
			}

			string authLogin = GetInternalAuthLogin();
			string authToken = GetInternalAuthToken();
			string uid = GetUserId();

			if (string.IsNullOrEmpty(authLogin) ||
				string.IsNullOrEmpty(authToken) ||
				string.IsNullOrEmpty(uid))
			{
				_logger.LogTrace("Failed connecting bindings. Internal auth data is empty.");

				callback?.Invoke(this, SnipeErrorCodes.PARAMS_WRONG);
				return;
			}

			string connectProviderId = anotherBinding.ProviderId;
			string connectUserId = anotherBinding.GetUserId();

			if (!string.IsNullOrEmpty(connectUserId))
			{
				RequestConnectAuths(uid, pass, connectProviderId, connectUserId, callback);
				return;
			}

			_logger.LogTrace("Another user ID is not ready. Trying to fetch...");

			anotherBinding.Fetcher.Fetch(false, anotherUserId =>
			{
				if (string.IsNullOrEmpty(anotherUserId))
				{
					_logger.LogTrace("Failed connecting bindings. The fetched user ID is empty.");

					callback?.Invoke(this, SnipeErrorCodes.PARAMS_WRONG);
					return;
				}

				// Don't use the returned raw value directly.
				// Use GetUserId() to get the formatted one instead.
				connectUserId = anotherBinding.GetUserId();
				RequestConnectAuths(uid, pass, connectProviderId, connectUserId, callback);
			});
		}

		private void RequestConnectAuths(string uid, string pass, string connectProviderId, string connectLogin, BindResultCallback callback)
		{
			var data = new Dictionary<string, object>()
			{
				["ckey"] = GetClientKey(),
				["provider"] = ProviderId,
				["login"] = uid,
				["auth"] = pass,
				["connectLogin"] = connectLogin,
				["connectProvider"] = connectProviderId,
			};

			FillExtraParameters(data);

			new UnauthorizedRequest(_communicator, _services, SnipeMessageTypes.AUTH_CONNECT, data)
				.Request((string errorCode, IDictionary<string, object> _) =>
				{
					errorCode = NormalizeCallbackErrorCode(errorCode);
					callback?.Invoke(this, errorCode);
				});
		}

		private static string NormalizeCallbackErrorCode(string errorCode)
		{
			return errorCode switch
			{
				"bindExists" => SnipeErrorCodes.OK,
				_ => errorCode
			};
		}
	}
}
