
namespace MiniIT.Snipe
{
    public class AuthProviderFactoryImpl : AuthProviderFactory
    {
        public override ProviderType Create<ProviderType>() where ProviderType : AuthProvider, new()
		{
            var provider_type = typeof(ProviderType);
            if (provider_type.Equals(typeof(AppleGameCenterAuthProvider)) || provider_type.IsSubclassOf(typeof(AppleGameCenterAuthProvider)))
            {
                return new AppleGameCenterAuthProviderImpl();
            }

            return new ProviderType();
		}
    }

}