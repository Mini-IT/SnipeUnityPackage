
namespace MiniIT.Snipe
{
	public class UnauthorizedRequest : AbstractCommunicatorRequest
	{
		public UnauthorizedRequest(SnipeCommunicator communicator, string messageType = null, SnipeObject data = null)
			: base(communicator, messageType, data)
		{
		}

		public override bool Equals(object obj) => obj is UnauthorizedRequest && base.Equals(obj);
	}
}
