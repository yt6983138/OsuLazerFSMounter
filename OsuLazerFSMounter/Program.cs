namespace OsuLazerFSMounter;
internal class Program
{
	private static int Main(string[] args)
	{
		OsuVFSService service = new();
		return service.Run();
	}
}
