namespace MiniIT.Snipe
{
	public interface IAuthIdFetcherWithToken : IAuthIdFetcher
	{
		public string Token { get; }
	}
}
