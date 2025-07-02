namespace MiniIT.Snipe
{
	public interface ISnipeContextProvider
	{
		/// <summary>
		/// Try to get the instance of <see cref="SnipeContext"/>.
		/// If the internal reference is not set yet,
		/// then <b>no instance will be created</b>
		/// </summary>
		/// <param name="id">Context ID</param>
		/// <param name="context">Instance of <see cref="SnipeContext"/></param>
		/// <returns><c>true</c> if a valid intance is found</returns>
		bool TryGetContext(int id, out SnipeContext context);

		/// <summary>
		/// Try to get the default instance of <see cref="SnipeContext"/> (<c>context id = 0</c>).
		/// If the internal reference is not set yet,
		/// then <b>no instance will be created</b>
		/// </summary>
		/// <param name="context">Instance of <see cref="SnipeContext"/></param>
		/// <returns><c>true</c> if a valid intance is found</returns>
		bool TryGetContext(out SnipeContext context);

		/// <summary>
		/// Gets or creates <see cref="SnipeContext"/> with the ID == <paramref name="id"/>
		/// </summary>
		/// <param name="id">Context ID</param>
		/// <returns>Instance of <see cref="SnipeContext"/></returns>
		SnipeContext GetOrCreateContext(int id = 0);

		/// <summary>
		/// Returns an instance of <see cref="ISnipeContextReference"/> with the given ID
		/// </summary>
		/// <param name="id">Context ID</param>
		/// <returns></returns>
		ISnipeContextReference GetContextReference(int id = 0);
	}
}
