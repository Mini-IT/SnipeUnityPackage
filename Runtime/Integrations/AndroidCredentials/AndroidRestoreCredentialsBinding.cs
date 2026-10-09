using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Utils;
using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	/// <summary>
	/// Restores a device binding after login, then registers one restore key per account and install.
	/// Call ClearAsync before signing out, switching accounts or changing the Snipe project/cluster.
	/// </summary>
	public class AndroidRestoreCredentialsBinding : AuthBinding, IConnectableAuthBinding
	{
		private const string REQUEST_GET_CHALLENGE = "google.restoreKeyChallenge";
		private const string REQUEST_LOGIN = "google.restoreKeyLogin";
		private const string REQUEST_GET_OPTIONS = "google.restoreKeyOptions";
		private const string REQUEST_REGISTER = "google.restoreKeyRegister";
		private const int REQUEST_TIMEOUT_MS = 3000;
		private const int NATIVE_TIMEOUT_MS = 60000;

		private readonly AuthBinding _deviceBinding;
		private CancellationTokenSource _operationCancellation;
		private Task<string> _operation;
		private TaskCompletionSource<string> _restoreCompletion;
		private int _operationUserId;
		private string _pendingAssertion;
		private bool _restoreChecked;
		private bool _clearing;
		private bool _disposed;

		public AndroidRestoreCredentialsBinding(ISnipeServices services)
			: this(services, null)
		{
		}

		protected AndroidRestoreCredentialsBinding(ISnipeServices services, AuthBinding deviceBinding)
			: base("google.restoreKey", null, services)
		{
			_deviceBinding = deviceBinding;
		}

		protected virtual bool IsSupported
		{
			get
			{
#if UNITY_ANDROID && !AMAZON_STORE && !UNITY_EDITOR
				return true;
#else
				return false;
#endif
			}
		}

		public override void Initialize(int contextId, ISnipeCommunicator communicator,
			AuthSubsystem authSubsystem, Func<string> getClientKeyMethod)
		{
			CancelOperation();
			Unsubscribe();
			base.Initialize(contextId, communicator, authSubsystem, getClientKeyMethod);
			_disposed = false;
			_restoreChecked = false;
			if (_communicator != null)
			{
				_communicator.ConnectionClosed += CancelOperation;
				_communicator.ConnectionDisrupted += CancelOperation;
			}
		}

		public override void Start()
		{
			Bind();
		}

		public override void Bind(BindResultCallback callback = null)
		{
			_services.MainThreadRunner.RunInMainThread(() =>
			{
				if (_disposed || _clearing || !IsSupported || _communicator?.LoggedIn != true)
				{
					callback?.Invoke(this, SnipeErrorCodes.NOT_READY);
					return;
				}

				if (_operation != null && !_operation.IsCompleted && _operationCancellation.IsCancellationRequested)
				{
					_ = RestartAfterCancellationAsync(_operation, _authSubsystem.UserID, callback);
					return;
				}

				if (_operation == null || _operation.IsCompleted)
				{
					var completion = new TaskCompletionSource<string>();
					var cancellation = new CancellationTokenSource();
					_operation = completion.Task;
					_operationCancellation = cancellation;
					_operationUserId = _authSubsystem.UserID;
					_ = RunOperationAsync(_operationUserId, cancellation, completion);
				}

				_ = NotifyAsync(_operation, callback);
			});
		}

		private async Task RestartAfterCancellationAsync(Task<string> previous, int userId, BindResultCallback callback)
		{
			await previous;
			if (_disposed || _authSubsystem.UserID != userId)
			{
				callback?.Invoke(this, SnipeErrorCodes.NOT_READY);
				return;
			}
			Bind(callback);
		}

		private async Task NotifyAsync(Task<string> operation, BindResultCallback callback)
		{
			string errorCode = await operation;
			callback?.Invoke(this, errorCode);
		}

		private async Task RunOperationAsync(int userId, CancellationTokenSource cancellation, TaskCompletionSource<string> completion)
		{
			string errorCode;
			try
			{
				errorCode = await RunCycleAsync(userId, cancellation.Token);
			}
			catch (OperationCanceledException)
			{
				errorCode = SnipeErrorCodes.NOT_READY;
			}
			catch (Exception e)
			{
				_logger.LogWarning("Restore credentials operation failed: {0}", e.GetType().Name);
				errorCode = SnipeErrorCodes.INVALID_DATA;
			}
			finally
			{
				_operationCancellation = null;
				_restoreCompletion = null;
				_pendingAssertion = null;
				cancellation.Dispose();
			}
			completion.TrySetResult(errorCode);
		}

		private async Task<string> RunCycleAsync(int userId, CancellationToken token)
		{
			EnsureCurrentUser(userId, token);
			string path = GetRegistrationMarkerPath();
			if (File.Exists(path))
			{
				if (File.ReadAllText(path) == $"{ContextId}:{userId}")
				{
					return SnipeErrorCodes.OK;
				}
				bool deleted;
				try
				{
					deleted = await DeleteNativeCredentialAsync(token);
				}
				catch (OperationCanceledException)
				{
					File.Delete(path);
					throw;
				}
				if (!deleted)
				{
					return SnipeErrorCodes.NOT_READY;
				}
				File.Delete(path);
				EnsureCurrentUser(userId, token);
				_restoreChecked = true;
			}

			if (!_restoreChecked)
			{
				AuthBinding deviceBinding = _deviceBinding;
				if (deviceBinding == null && _authSubsystem.TryGetBinding<DeviceIdBinding>(out var registeredBinding))
				{
					deviceBinding = registeredBinding;
				}
				if (deviceBinding?.Fetcher == null || deviceBinding.ProviderId != "dvid")
				{
					return SnipeErrorCodes.NOT_READY;
				}

				var challenge = await RequestAsync(REQUEST_GET_CHALLENGE,
					new Dictionary<string, object>() { ["ckey"] = GetClientKey() }, userId, token);
				if (challenge.ErrorCode != SnipeErrorCodes.OK)
				{
					return challenge.ErrorCode;
				}
				string options = challenge.Data.SafeGetString("options");
				if (string.IsNullOrWhiteSpace(options))
				{
					return SnipeErrorCodes.INVALID_DATA;
				}

				using var nativeCancellation = CreateNativeCancellation(token);
				var read = await ReadCredentialAsync(options, nativeCancellation.Token);
				nativeCancellation.Token.ThrowIfCancellationRequested();
				EnsureCurrentUser(userId, token);
				_logger.LogDebug("Restore credentials read: {0}", read.Status);
				if (read.Status == AndroidCredentialsStatus.Success)
				{
					if (string.IsNullOrWhiteSpace(read.Json))
					{
						return SnipeErrorCodes.INVALID_DATA;
					}
					_pendingAssertion = read.Json;
					var restoreCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
					_restoreCompletion = restoreCompletion;
					using var registration = token.Register(() => restoreCompletion.TrySetCanceled(token));
					_authSubsystem.ConnectAndRelogin(deviceBinding, this);
					string errorCode = await restoreCompletion.Task;
					if (errorCode == SnipeErrorCodes.OK)
					{
						return errorCode;
					}
					EnsureCurrentUser(userId, token);
					if (errorCode != "noSuchRestoreKey" && errorCode != "authError")
					{
						return errorCode;
					}
					if (!await DeleteNativeCredentialAsync(token))
					{
						return SnipeErrorCodes.NOT_READY;
					}
				}
				else if (read.Status != AndroidCredentialsStatus.NotFound)
				{
					return SnipeErrorCodes.NOT_READY;
				}
				EnsureCurrentUser(userId, token);
				_restoreChecked = true;
			}

			var creation = await RequestAsync(REQUEST_GET_OPTIONS, null, userId, token);
			if (creation.ErrorCode != SnipeErrorCodes.OK)
			{
				return creation.ErrorCode;
			}
			string creationOptions = creation.Data.SafeGetString("options");
			if (string.IsNullOrWhiteSpace(creationOptions))
			{
				return SnipeErrorCodes.INVALID_DATA;
			}
			using var saveCancellation = CreateNativeCancellation(token);
			var save = await SaveCredentialAsync(creationOptions, saveCancellation.Token);
			saveCancellation.Token.ThrowIfCancellationRequested();
			EnsureCurrentUser(userId, token);
			_logger.LogDebug("Restore credentials save: {0}", save.Status);
			if (save.Status != AndroidCredentialsStatus.Success || string.IsNullOrWhiteSpace(save.Json))
			{
				return SnipeErrorCodes.NOT_READY;
			}
			var register = await RequestAsync(REQUEST_REGISTER,
				new Dictionary<string, object>() { ["response"] = save.Json }, userId, token);
			if (register.ErrorCode == SnipeErrorCodes.OK)
			{
				EnsureCurrentUser(userId, token);
				WriteRegistrationMarker(path, userId);
			}
			return register.ErrorCode;
		}

		void IConnectableAuthBinding.Connect(AuthBinding anotherBinding, BindResultCallback callback)
		{
			_ = ConnectAsync(anotherBinding, callback);
		}

		private async Task ConnectAsync(AuthBinding deviceBinding, BindResultCallback callback)
		{
			var completion = _restoreCompletion;
			string errorCode = SnipeErrorCodes.NOT_READY;
			try
			{
				string deviceId = deviceBinding.GetUserId();
				if (completion == null || string.IsNullOrEmpty(_pendingAssertion) || string.IsNullOrEmpty(deviceId))
				{
					errorCode = SnipeErrorCodes.INVALID_DATA;
				}
				else
				{
					var response = await RequestAsync(REQUEST_LOGIN, new Dictionary<string, object>()
					{
						["ckey"] = GetClientKey(),
						["response"] = _pendingAssertion,
						["deviceID"] = deviceId,
					}, _operationUserId, _operationCancellation.Token);
					errorCode = response.ErrorCode;
					if (errorCode == SnipeErrorCodes.OK)
					{
						if (response.Data.SafeGetValue<int>("id") <= 0 || response.Data.SafeGetString("deviceID") != deviceId)
						{
							errorCode = SnipeErrorCodes.INVALID_DATA;
						}
						else
						{
							_restoreChecked = true;
						}
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				_logger.LogWarning("Restore credentials login failed: {0}", e.GetType().Name);
			}
			completion?.TrySetResult(errorCode);
			callback?.Invoke(this, errorCode);
		}

		private async Task<(string ErrorCode, IDictionary<string, object> Data)> RequestAsync(
			string messageType, IDictionary<string, object> data, int userId, CancellationToken token)
		{
			EnsureCurrentUser(userId, token);
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
			timeout.CancelAfter(REQUEST_TIMEOUT_MS);
			var completion = new TaskCompletionSource<(string, IDictionary<string, object>)>(TaskCreationOptions.RunContinuationsAsynchronously);
			using var request = new UnauthorizedRequest(_communicator, _services, messageType, data);
			using var registration = timeout.Token.Register(() =>
				_services.MainThreadRunner.RunInMainThread(() =>
				{
					request.Dispose();
					completion.TrySetCanceled();
				}));
			request.Request((errorCode, response) => completion.TrySetResult((errorCode, response)));
			var result = await completion.Task;
			EnsureCurrentUser(userId, token);
			_logger.LogDebug("Restore credentials {0}: {1}", messageType, result.Item1);
			return result;
		}

		private void EnsureCurrentUser(int userId, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			if (_disposed || _communicator?.LoggedIn != true || userId <= 0 || _authSubsystem.UserID != userId)
			{
				throw new OperationCanceledException();
			}
		}

		private static CancellationTokenSource CreateNativeCancellation(CancellationToken token)
		{
			var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
			cancellation.CancelAfter(NATIVE_TIMEOUT_MS);
			return cancellation;
		}

		private async Task<bool> DeleteNativeCredentialAsync(CancellationToken token)
		{
			using var cancellation = CreateNativeCancellation(token);
			var status = await DeleteCredentialAsync(cancellation.Token);
			cancellation.Token.ThrowIfCancellationRequested();
			token.ThrowIfCancellationRequested();
			_logger.LogDebug("Restore credentials delete: {0}", status);
			return status == AndroidCredentialsStatus.Success;
		}

		/// <summary>Clears the local registration marker and credential before an explicit account change.</summary>
		public Task<bool> ClearAsync()
		{
			var completion = new TaskCompletionSource<bool>();
			_services.MainThreadRunner.RunInMainThread(() =>
			{
				_ = ClearCoreAsync(completion);
			});
			return completion.Task;
		}

		private async Task ClearCoreAsync(TaskCompletionSource<bool> completion)
		{
			if (_disposed || _clearing)
			{
				completion.TrySetResult(false);
				return;
			}
			if (!IsSupported)
			{
				completion.TrySetResult(true);
				return;
			}
			_clearing = true;
			bool cleared = false;
			try
			{
				CancelOperation();
				if (_operation != null)
				{
					await _operation;
				}
				File.Delete(GetRegistrationMarkerPath());
				_restoreChecked = true;
				using var cancellation = new CancellationTokenSource();
				_operationCancellation = cancellation;
				cleared = await DeleteNativeCredentialAsync(cancellation.Token);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				_logger.LogWarning("Restore credentials clear failed: {0}", e.GetType().Name);
			}
			finally
			{
				_operationCancellation = null;
				_clearing = false;
			}
			completion.TrySetResult(cleared);
		}

		private void WriteRegistrationMarker(string path, int userId)
		{
			string temporaryPath = path + ".tmp";
			try
			{
				File.WriteAllText(temporaryPath, $"{ContextId}:{userId}");
				if (File.Exists(path))
				{
					File.Replace(temporaryPath, path, null);
				}
				else
				{
					File.Move(temporaryPath, path);
				}
			}
			finally
			{
				File.Delete(temporaryPath);
			}
		}

		protected virtual string GetRegistrationMarkerPath()
		{
			using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
			using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
			using var context = activity.Call<AndroidJavaObject>("getApplicationContext");
			using var directory = context.Call<AndroidJavaObject>("getNoBackupFilesDir");
			return Path.Combine(directory.Call<string>("getAbsolutePath"), "snipe-restore-key.txt");
		}

		protected virtual async Task<(AndroidCredentialsStatus Status, string Json)> ReadCredentialAsync(string options, CancellationToken token)
		{
			var result = await AndroidCredentials.ReadAsync(options, token);
			return (result.Status, result.ResponseJson);
		}

		protected virtual async Task<(AndroidCredentialsStatus Status, string Json)> SaveCredentialAsync(string options, CancellationToken token)
		{
			var result = await AndroidCredentials.SaveAsync(options, token);
			return (result.Status, result.ResponseJson);
		}

		protected virtual async Task<AndroidCredentialsStatus> DeleteCredentialAsync(CancellationToken token)
		{
			return (await AndroidCredentials.DeleteAsync(token)).Status;
		}

		private void CancelOperation()
		{
			var cancellation = _operationCancellation;
			_services.MainThreadRunner.RunInMainThread(() =>
			{
				if (_operationCancellation == cancellation)
				{
					cancellation?.Cancel();
				}
			});
		}

		private void Unsubscribe()
		{
			if (_communicator != null)
			{
				_communicator.ConnectionClosed -= CancelOperation;
				_communicator.ConnectionDisrupted -= CancelOperation;
			}
		}

		public override void Dispose()
		{
			_disposed = true;
			CancelOperation();
			Unsubscribe();
			base.Dispose();
		}
	}
}
