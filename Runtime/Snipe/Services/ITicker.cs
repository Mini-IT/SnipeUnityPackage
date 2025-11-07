using System;

namespace MiniIT.Snipe
{
	public interface ITicker
	{
		event Action OnTick;
	}
}
