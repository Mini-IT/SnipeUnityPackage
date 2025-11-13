using Microsoft.Extensions.Logging;
using System;

namespace MiniIT.Snipe.Unity
{
    public sealed class WebGLIdFetcher : AuthIdFetcher
    {
        private const string ID_PREFS_KEY = "com.miniit.app.webgl.id";
		private readonly ILogger _logger;

		public WebGLIdFetcher()
		{
			_logger = SnipeServices.LogService.GetLogger(nameof(WebGLIdFetcher));
		}

		public override void Fetch(bool _, Action<string> callback = null)
		{
			if (string.IsNullOrEmpty(Value))
			{
				SetId();
			}

			callback?.Invoke(Value);
		}

        private void SetId()
        {
            string defaultValue = Guid.NewGuid().ToString();
            string savedValue = UnityEngine.PlayerPrefs.GetString(ID_PREFS_KEY, defaultValue);

            SetValue(savedValue);
        }

        private void SetValue(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                UnityEngine.PlayerPrefs.SetString(ID_PREFS_KEY, value);

                Value = value;
                _logger.LogTrace($"[WebGLIdFetcher] Value = {Value}");
            }
            else
            {
                Value = string.Empty;
                _logger.LogTrace($"[WebGLIdFetcher] Value isn't valid");
            }
        }
	}
}