namespace OsuLazerFSMounter;
public class OsuVFSOption
{
	public bool ReadOnly { get; init; } = false;
	/// <summary>
	/// if set to true, the vfs may use underlying file directly for writing without copying to another location.
	/// this may cause data loss if the underlying file is modified by other process while writing.
	/// </summary>
	public bool AllowDirectFileWriting { get; init; } = false;
	public string VolumeLabel { get; init; } = nameof(OsuVFS);
}
