using OsuLazerFSMounter.Utility;

namespace OsuLazerFSMounter.FileSystem;
public interface IDescriptor : IDisposable
{
	ScopedSemaphoreSlim Lock { get; }
	IVirtualFileSystemObject VirtualObject { get; }
	bool DeleteOnClose { get; set; }
}
