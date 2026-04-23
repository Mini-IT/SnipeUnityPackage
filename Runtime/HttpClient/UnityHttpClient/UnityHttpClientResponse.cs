using System;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace MiniIT.Http
{
	public struct UnityHttpClientResponse : IHttpClientResponse
	{
		private const long REQUEST_TIMEOUT_RESPONSE_CODE = 408;
		private const string REQUEST_TIMEOUT_ERROR = "408 RequestTimeout";

		public long ResponseCode { get; }

		public bool IsSuccess { get; }
		public string Error { get; }

		private readonly byte[] _content;
		private readonly string _contentType;

		public UnityHttpClientResponse(UnityWebRequest unityWebRequest) : this()
		{
			ResponseCode = unityWebRequest.responseCode;
			IsSuccess = string.IsNullOrEmpty(unityWebRequest.error);
			Error = unityWebRequest.error;
			_content = unityWebRequest.downloadHandler?.data;
			_contentType = unityWebRequest.GetResponseHeader("Content-Type");
		}

		private UnityHttpClientResponse(long responseCode, bool isSuccess, string error, byte[] content, string contentType) : this()
		{
			ResponseCode = responseCode;
			IsSuccess = isSuccess;
			Error = error;
			_content = content;
			_contentType = contentType;
		}

		public static UnityHttpClientResponse CreateTimeout()
		{
			return new UnityHttpClientResponse(REQUEST_TIMEOUT_RESPONSE_CODE, false, REQUEST_TIMEOUT_ERROR, null, null);
		}

		public UniTask<string> GetStringContentAsync()
		{
			if (_content == null)
			{
				return UniTask.FromResult<string>(null);
			}

			string content = GetContentEncoding().GetString(_content);
			return UniTask.FromResult(content);
		}

		public UniTask<byte[]> GetBinaryContentAsync()
		{
			return UniTask.FromResult(_content);
		}

		public void Dispose()
		{
		}

		private Encoding GetContentEncoding()
		{
			if (string.IsNullOrEmpty(_contentType))
			{
				return Encoding.UTF8;
			}

			const string PREFIX = "charset=";

			int start = _contentType.IndexOf(PREFIX, StringComparison.OrdinalIgnoreCase);
			if (start < 0)
			{
				return Encoding.UTF8;
			}

			start += PREFIX.Length;

			int end = _contentType.IndexOf(';', start);
			string charset = end >= 0
				? _contentType.Substring(start, end - start)
				: _contentType.Substring(start);

			charset = charset.Trim().Trim('"');
			if (charset.Length == 0)
			{
				return Encoding.UTF8;
			}

			try
			{
				return Encoding.GetEncoding(charset);
			}
			catch (ArgumentException)
			{
				return Encoding.UTF8;
			}
		}
	}
}
