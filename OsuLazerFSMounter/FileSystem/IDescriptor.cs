namespace OsuLazerFSMounter.FileSystem;
public interface IDescriptor : IDisposable
{
	ScopedSemaphoreSlim Lock { get; }
	IVirtualFileSystemObject VirtualObject { get; }
}
