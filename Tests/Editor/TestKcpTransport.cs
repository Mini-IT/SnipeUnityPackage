using MiniIT.Snipe.Configuration;
using NUnit.Framework;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestKcpTransport
	{
		[Test]
		public void RecoveredConnection_RaisesRecoveryWithoutOpeningAgain()
		{
			var services = new NullSnipeServices();
			var options = new SnipeOptions(0, new SnipeOptionsData(), services);
			int openedCount = 0;
			int recoveredCount = 0;
			var transport = new KcpTransport(new TransportOptions()
			{
				SnipeOptions = options,
				SnipeServices = services,
				ConnectionOpenedHandler = _ => openedCount++
			});
			transport.ConnectionRecovered += _ => recoveredCount++;

			transport.OnClientConnected();
			Assert.AreEqual(1, openedCount);
			Assert.AreEqual(0, recoveredCount);

			transport.OnClientConnected();

			Assert.AreEqual(1, openedCount);
			Assert.AreEqual(1, recoveredCount);
		}

		[Test]
		public void DisconnectedTransport_CanRaiseConnectionOpenedAgain()
		{
			var services = new NullSnipeServices();
			var options = new SnipeOptions(0, new SnipeOptionsData(), services);
			int openedCount = 0;
			var transport = new KcpTransport(new TransportOptions()
			{
				SnipeOptions = options,
				SnipeServices = services,
				ConnectionOpenedHandler = _ => openedCount++
			});

			transport.OnClientConnected();
			transport.Disconnect();
			transport.OnClientConnected();

			Assert.AreEqual(2, openedCount);
		}
	}
}
