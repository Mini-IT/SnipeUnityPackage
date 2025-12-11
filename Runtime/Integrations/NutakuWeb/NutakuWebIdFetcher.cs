#if NUTAKU_WEB
using Cysharp.Threading.Tasks;
using MiniIT.Threading;
using System;

namespace MiniIT.Snipe.Unity
{
    public sealed class NutakuWebIdFetcher : AuthIdFetcher, IAuthIdFetcherWithToken
    {
        private const int INITIALIZATION_DELAY = 100;
		public string Token { get; private set; }

		public override void Fetch(bool wait_initialization, Action<string> callback = null)
        {
            if (wait_initialization && string.IsNullOrEmpty(Value))
            {
                WaitForInitialization(callback).Forget();
                return;
            }

            SetId();
            GetHandshake();

            callback?.Invoke(Value);
        }

		private async UniTaskVoid WaitForInitialization(Action<string> callback)
		{
			while (!NutakuMono.Instance.IsInitialized)
			{
				await AlterTask.Delay(INITIALIZATION_DELAY);
			}

			SetId();
			GetHandshake();

			callback?.Invoke(Value);
		}

		private void GetHandshake()
        {
            SetToken(NutakuMono.Instance.HandshakeToken);
        }

        private void SetId()
        {
            SetId(NutakuMono.Instance.UserId);
        }

        private void SetToken(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Token = value;
            }
            else
            {
                Token = string.Empty;
            }
        }

        private void SetId(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Value = value;
            }
            else
            {
                Value = string.Empty;
            }
        }
    }
}
#endif
