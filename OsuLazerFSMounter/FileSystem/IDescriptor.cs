using OsuLazerFSMounter.Utility;

namespace OsuLazerFSMounter.FileSystem;
public interface IDescriptor : IDisposable
{
	bool Invalidated { get; }
	ScopedSemaphoreSlim Lock { get; }
	IVirtualFileSystemObject VirtualObject { get; }
	bool DeleteOnClose { get; set; }
	/// <summary>
	/// if you use this method, the Close() method will directly skip processing, and only dispose/remove the descriptor from opened descriptors list.
	/// </summary>
	void Invalidate();
}
