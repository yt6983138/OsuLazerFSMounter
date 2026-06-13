namespace OsuLazerFSMounter;
public class OsuVFSOption
{
	public bool ReadOnly { get; init; } = false;
	public string VolumeLabel { get; init; } = nameof(OsuVFS);
}
