namespace OsuLazerFSMounter.FileSystem;
public record class ReadDirectoryContext(VirtualDirectory Directory)
{
	public int LastPosition { get; set; } = 0;
}
