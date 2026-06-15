using OsuLazerFSMounter.Utility;

namespace OsuLazerFSMounter.FileSystem;

public class DirectoryDescriptor : IDescriptor
{
	public VirtualDirectory Directory { get; set; }
	public ScopedSemaphoreSlim Lock { get; } = new(1, 1);
	public bool DeleteOnClose { get; set; }

	IVirtualFileSystemObject IDescriptor.VirtualObject => this.Directory;

	public DirectoryDescriptor(VirtualDirectory directory)
	{
		this.Directory = directory;
	}

	public void Dispose()
	{
		this.Lock.Dispose();
		GC.SuppressFinalize(this);
	}
}
