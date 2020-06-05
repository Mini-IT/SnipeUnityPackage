
namespace MiniIT.Snipe
{
    public class AuthProviderFactory
    {
        public static ProviderType Create<ProviderType>() where ProviderType : AuthProvider, new()
		{
            return new ProviderType();
		}
    }

}