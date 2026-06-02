namespace OsuLazerFSMounter.FileSystem;

public class FileDescriptor : IDescriptor
{
	public VirtualFile File { get; set; }
	public FileStream Stream { get; set; }
	public ScopedSemaphoreSlim Lock { get; } = new(1, 1);
	public bool DeleteOnClose { get; set; }

	IVirtualFileSystemObject IDescriptor.VirtualObject => this.File;

	public FileDescriptor(VirtualFile file, FileStream stream)
	{
		this.File = file;
		this.Stream = stream;
	}

	public void Dispose()
	{
		this.Lock.Dispose();
		this.Stream.Dispose();
		GC.SuppressFinalize(this);
	}
}
