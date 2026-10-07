using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MiniIT.Http;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestUnityHttpClient
	{
		[UnityTest]
		public IEnumerator Get_CancelledFromWorkerThread_ReturnsTimeout()
		{
			using var cancellation = new CancellationTokenSource();
			var requestReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();

			try
			{
				var endpoint = (IPEndPoint)listener.LocalEndpoint;
				ThreadPool.QueueUserWorkItem(_ => HoldConnection(listener, requestReceived, cancellation.Token));

				var client = new UnityHttpClient();
				UniTask<IHttpClientResponse> request = client.Get(new Uri($"http://127.0.0.1:{endpoint.Port}/"), cancellation.Token);

				yield return requestReceived.Task.AsUniTask().ToCoroutine();
				ThreadPool.QueueUserWorkItem(_ => cancellation.Cancel());

				IHttpClientResponse response = null;
				yield return request.ToCoroutine(value => response = value);

				Assert.AreEqual(408, response.ResponseCode);
				Assert.IsFalse(response.IsSuccess);
			}
			finally
			{
				listener.Stop();
			}
		}

		[UnityTest]
		public IEnumerator Get_Timeout_ReturnsTimeout()
		{
			using var serverCancellation = new CancellationTokenSource();
			var requestReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();

			try
			{
				var endpoint = (IPEndPoint)listener.LocalEndpoint;
				ThreadPool.QueueUserWorkItem(_ => HoldConnection(listener, requestReceived, serverCancellation.Token));

				var client = new UnityHttpClient();
				UniTask<IHttpClientResponse> request = client.Get(new Uri($"http://127.0.0.1:{endpoint.Port}/"), TimeSpan.FromMilliseconds(100));

				yield return requestReceived.Task.AsUniTask().ToCoroutine();

				IHttpClientResponse response = null;
				yield return request.ToCoroutine(value => response = value);

				Assert.AreEqual(408, response.ResponseCode);
				Assert.IsFalse(response.IsSuccess);
			}
			finally
			{
				serverCancellation.Cancel();
				listener.Stop();
			}
		}

		private static void HoldConnection(TcpListener listener, TaskCompletionSource<bool> requestReceived, CancellationToken cancellationToken)
		{
			try
			{
				using TcpClient connection = listener.AcceptTcpClient();
				requestReceived.TrySetResult(true);
				cancellationToken.WaitHandle.WaitOne();
			}
			catch (SocketException)
			{
			}
		}
	}
}
