namespace MiniIT.Snipe.Diagnostics
{
	public interface IDiagnosticsService
	{
		IDiagnosticsChannel GetChannel(string name);
	}
}
