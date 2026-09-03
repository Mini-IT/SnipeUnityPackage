using System;
using System.IO;
using System.Net;
using System.Text;
using Cysharp.Threading.Tasks;
using MiniIT.Http;
using MiniIT.Snipe.Logging;
using UnityEngine;

namespace MiniIT.Snipe.Internal
{
	internal interface ILogFileSender
	{
		UniTask<bool> SendAsync(StreamReader file);
	}

	internal sealed class LogBatchContent
	{
		internal string Content;
		internal int RecordCount;
		internal int PayloadBytes;
		internal bool HasOversizedRecord;
		internal int OversizedRecordBytes;
	}

	internal readonly struct LogSendProfile
	{
		internal int MaxChunkBytes { get; }
		internal TimeSpan RequestTimeout { get; }

		internal LogSendProfile(int maxChunkBytes, TimeSpan requestTimeout)
		{
			MaxChunkBytes = maxChunkBytes;
			RequestTimeout = requestTimeout;
		}
	}

	internal sealed class LogSender : ILogFileSender
	{
		private const int DEFAULT_MAX_CHUNK_BYTES = 200 * 1024;
		private const int WEB_GL_MAX_CHUNK_BYTES = 4 * 1024;
		private const int DEFAULT_REQUEST_TIMEOUT_SECONDS = 5;
		private const int WEB_GL_REQUEST_TIMEOUT_SECONDS = 20;

		private static readonly UTF8Encoding s_utf8NoBom = new UTF8Encoding(false);

		private readonly SnipeContext _snipeContext;
		private readonly SnipeOptions _snipeOptions;
		private readonly ISnipeServices _services;
		private readonly int? _sessionID;

		internal LogSender(SnipeContext snipeContext, SnipeOptions snipeOptions, ISnipeServices services, int? sessionID)
		{
			_snipeContext = snipeContext;
			_snipeOptions = snipeOptions;
			_services = services;
			_sessionID = sessionID;
		}

		public async UniTask<bool> SendAsync(StreamReader file)
		{
			if (file == null)
			{
				throw new ArgumentNullException(nameof(file));
			}

			if (_services == null)
			{
				DebugLogger.LogError($"{SnipeLogPipeline.DiagnosticLogPrefix} Missing services for log sender.");
				return false;
			}

			string apiKey = _snipeOptions?.ClientKey;
			string url = _snipeOptions?.LogReporterUrl;
			if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(url))
			{
				DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Invalid apiKey or url.");
				return false;
			}

			int connectionId = 0;
			int userId = 0;
			if (_snipeContext?.Communicator != null)
			{
				int.TryParse(_snipeContext.Communicator.ConnectionId, out connectionId);
				userId = _snipeContext.Auth?.UserID ?? 0;
			}

			LogSendProfile profile = GetSendProfile(Application.platform);
			IHttpClient httpClient = _services.HttpClientFactory.CreateHttpClient();
			httpClient.SetAuthToken(apiKey);

			try
			{
				return await SendBatchesAsync(
					file,
					connectionId,
					_sessionID,
					userId,
					Application.version,
					Application.platform.ToString(),
					profile.MaxChunkBytes,
					async (batch, portionIndex) =>
					{
						if (batch.HasOversizedRecord)
						{
							DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Oversized log record detected. portion={portionIndex} recordBytes={batch.OversizedRecordBytes} maxChunkBytes={profile.MaxChunkBytes}");
						}

						DebugLogger.Log($"{SnipeLogPipeline.DiagnosticLogPrefix} Posting log portion. portion={portionIndex} recordCount={batch.RecordCount} payloadBytes={batch.PayloadBytes} timeoutSeconds={profile.RequestTimeout.TotalSeconds}");
						return await PostJsonAsync(httpClient, new Uri(url), batch.Content, profile.RequestTimeout);
					});
			}
			finally
			{
				if (httpClient is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}
		}

		internal static async UniTask<bool> SendBatchesAsync(
			StreamReader file,
			int connectionId,
			int? sessionID,
			int userId,
			string appVersion,
			string platform,
			int maxChunkBytes,
			Func<LogBatchContent, int, UniTask<bool>> sendPortionAsync)
		{
			if (file == null)
			{
				throw new ArgumentNullException(nameof(file));
			}

			if (sendPortionAsync == null)
			{
				throw new ArgumentNullException(nameof(sendPortionAsync));
			}

			string carryLine = null;
			int portionIndex = 0;
			while (!file.EndOfStream || !string.IsNullOrEmpty(carryLine))
			{
				LogBatchContent batch = BuildBatchContent(
					file,
					ref carryLine,
					connectionId,
					sessionID,
					userId,
					appVersion,
					platform,
					maxChunkBytes);

				if (batch == null)
				{
					continue;
				}

				portionIndex++;
				if (!await sendPortionAsync(batch, portionIndex))
				{
					return false;
				}
			}

			return true;
		}

		internal static LogBatchContent BuildBatchContent(
			StreamReader file,
			ref string carryLine,
			int connectionId,
			int? sessionID,
			int userId,
			string appVersion,
			string platform,
			int maxChunkBytes)
		{
			string prefix = BuildBatchPrefix(connectionId, sessionID, userId, appVersion, platform);
			const string suffix = "]}";
			int payloadBytes = s_utf8NoBom.GetByteCount(prefix) + s_utf8NoBom.GetByteCount(suffix);
			var builder = new StringBuilder(prefix);
			int recordCount = 0;
			bool oversized = false;
			int oversizedRecordBytes = 0;
			string pendingLine = carryLine;
			carryLine = null;

			while (true)
			{
				if (string.IsNullOrEmpty(pendingLine))
				{
					pendingLine = ReadNextNonEmptyLine(file);
				}

				if (string.IsNullOrEmpty(pendingLine))
				{
					break;
				}

				int recordBytes = s_utf8NoBom.GetByteCount(pendingLine);
				int separatorBytes = recordCount > 0 ? 1 : 0;
				if (payloadBytes + separatorBytes + recordBytes > maxChunkBytes)
				{
					if (recordCount > 0)
					{
						carryLine = pendingLine;
						break;
					}

					oversized = true;
					oversizedRecordBytes = recordBytes;
				}

				if (recordCount > 0)
				{
					builder.Append(',');
				}

				builder.Append(pendingLine);
				payloadBytes += separatorBytes + recordBytes;
				recordCount++;
				pendingLine = null;

				if (oversized)
				{
					break;
				}
			}

			if (recordCount == 0)
			{
				return null;
			}

			builder.Append(suffix);
			return new LogBatchContent
			{
				Content = builder.ToString(),
				RecordCount = recordCount,
				PayloadBytes = payloadBytes,
				HasOversizedRecord = oversized,
				OversizedRecordBytes = oversizedRecordBytes
			};
		}

		internal static string BuildBatchPrefix(int connectionId, int? sessionID, int userId, string appVersion, string platform)
		{
			var builder = new StringBuilder();
			builder.Append('{');
			builder.Append("\"connectionID\":");
			builder.Append(connectionId);
			builder.Append(',');
			if (sessionID.HasValue)
			{
				builder.Append("\"sessionID\":");
				builder.Append(sessionID.Value);
				builder.Append(',');
			}

			builder.Append("\"userID\":");
			builder.Append(userId);
			builder.Append(",\"version\":\"");
			builder.Append(SnipeLogPipeline.EscapeJson(appVersion));
			builder.Append("\",\"platform\":\"");
			builder.Append(SnipeLogPipeline.EscapeJson(platform));
			builder.Append("\",\"list\":[");
			return builder.ToString();
		}

		internal static LogSendProfile GetSendProfile(RuntimePlatform platform)
		{
			return platform == RuntimePlatform.WebGLPlayer
				? new LogSendProfile(WEB_GL_MAX_CHUNK_BYTES, TimeSpan.FromSeconds(WEB_GL_REQUEST_TIMEOUT_SECONDS))
				: new LogSendProfile(DEFAULT_MAX_CHUNK_BYTES, TimeSpan.FromSeconds(DEFAULT_REQUEST_TIMEOUT_SECONDS));
		}

		private static async UniTask<bool> PostJsonAsync(IHttpClient httpClient, Uri url, string content, TimeSpan timeout)
		{
			IHttpClientResponse response = null;
			try
			{
				response = await httpClient.PostJson(url, content, timeout);
				HttpStatusCode statusCode = (HttpStatusCode)response.ResponseCode;
				if (!response.IsSuccess)
				{
					DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed posting log portion. Result code = {(int)statusCode} {statusCode} {response.Error}");
					return false;
				}

				DebugLogger.Log($"{SnipeLogPipeline.DiagnosticLogPrefix} Send log portion result code = {(int)statusCode} {statusCode}");
				return true;
			}
			catch (Exception ex)
			{
				DebugLogger.LogError($"{SnipeLogPipeline.DiagnosticLogPrefix} Error posting log portion: {LogUtil.GetReducedException(ex)}");
				return false;
			}
			finally
			{
				response?.Dispose();
			}
		}

		private static string ReadNextNonEmptyLine(StreamReader file)
		{
			while (!file.EndOfStream)
			{
				string line = file.ReadLine();
				if (!string.IsNullOrEmpty(line))
				{
					return line;
				}
			}

			return null;
		}
	}
}
