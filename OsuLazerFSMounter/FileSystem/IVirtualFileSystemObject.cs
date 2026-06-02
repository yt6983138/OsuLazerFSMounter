namespace OsuLazerFSMounter.FileSystem;
public interface IVirtualFileSystemObject
{
	string Name { get; }
	VirtualDirectory? Parent { get; }
	VirtualPath GetFullPath();
}
