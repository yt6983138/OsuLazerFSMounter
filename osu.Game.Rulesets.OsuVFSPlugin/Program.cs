#if DEBUG
namespace osu.Game.Rulesets.OsuVFSPlugin;

internal class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		osu.Desktop.Program.Main(args);
	}
}
#endif
