namespace MiniIT.Snipe
{
	public interface ISnipeContextFactory
	{
		SnipeContext CreateContext(int id);
	}
}
