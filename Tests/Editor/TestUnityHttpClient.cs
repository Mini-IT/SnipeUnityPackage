using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
			using var requestReceived = new ManualResetEventSlim();
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();

			try
			{
				var endpoint = (IPEndPoint)listener.LocalEndpoint;
				ThreadPool.QueueUserWorkItem(_ => HoldConnection(listener, requestReceived, cancellation.Token));

				var client = new UnityHttpClient();
				UniTask<IHttpClientResponse> request = client.Get(new Uri($"http://127.0.0.1:{endpoint.Port}/"), cancellation.Token);

				yield return WaitUntil(() => requestReceived.IsSet);
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
			using var requestReceived = new ManualResetEventSlim();
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();

			try
			{
				var endpoint = (IPEndPoint)listener.LocalEndpoint;
				ThreadPool.QueueUserWorkItem(_ => HoldConnection(listener, requestReceived, serverCancellation.Token));

				var client = new UnityHttpClient();
				UniTask<IHttpClientResponse> request = client.Get(new Uri($"http://127.0.0.1:{endpoint.Port}/"), TimeSpan.FromMilliseconds(100));

				yield return WaitUntil(() => requestReceived.IsSet);

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

		private static void HoldConnection(TcpListener listener, ManualResetEventSlim requestReceived, CancellationToken cancellationToken)
		{
			try
			{
				using TcpClient connection = listener.AcceptTcpClient();
				requestReceived.Set();
				cancellationToken.WaitHandle.WaitOne();
			}
			catch (SocketException)
			{
			}
		}

		private static IEnumerator WaitUntil(Func<bool> condition)
		{
			const int MAX_WAIT_FRAMES = 60;
			for (int i = 0; i < MAX_WAIT_FRAMES; i++)
			{
				if (condition())
				{
					yield break;
				}

				yield return null;
			}

			Assert.Fail("Condition was not reached");
		}
	}
}
