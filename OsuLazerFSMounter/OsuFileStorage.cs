using osu.Framework.Platform;

namespace OsuLazerFSMounter;

public class OsuFileStorage : DesktopStorage
{
	public OsuFileStorage(string path)
		: base(path, null)
	{
	}
}
