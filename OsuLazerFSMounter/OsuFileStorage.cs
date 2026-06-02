using JetBrains.Annotations;
using osu.Framework.Platform;

namespace OsuLazerFSMounter;

public class OsuFileStorage : DesktopStorage
{
	public OsuFileStorage(string path)
		: base(path, null)
	{
	}

	public override Storage GetStorageForDirectory([NotNull] string path)
	{
		throw new NotSupportedException(
			"This operation is explicitly disabled to prevent the osu RealmAccess to clean up files, please ignore this");
	}
}
