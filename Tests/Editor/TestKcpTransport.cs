using MiniIT.Snipe.Configuration;
using NUnit.Framework;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestKcpTransport
	{
		[Test]
		public void RecoveredConnection_DoesNotRaiseConnectionOpenedAgain()
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
			transport.OnClientConnected();

			Assert.AreEqual(1, openedCount);
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
