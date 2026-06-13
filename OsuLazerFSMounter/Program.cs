namespace OsuLazerFSMounter;
internal class Program
{
	private static int Main(string[] args)
	{
		OsuVFSConsoleService service = new();
		return service.Run();
	}
}
