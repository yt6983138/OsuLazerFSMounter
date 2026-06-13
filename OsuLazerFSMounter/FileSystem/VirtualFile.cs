namespace OsuLazerFSMounter.FileSystem;

public class VirtualFile : IVirtualFileSystemObject
{
	public VirtualDirectory? Parent { get; internal set; }

	public string Name { get; set; }
	public string OriginalHash { get; set; }
	public FileInfo PhysicalFile
	{
		get => this.PhysicalFileLazy.Value;
		set => this.PhysicalFileLazy = new(value);
	}
	public Lazy<FileInfo> PhysicalFileLazy { get; set; }

	public VirtualFile(string name, string hash, FileInfo physicalFile)
		: this(name, hash, new Lazy<FileInfo>(physicalFile)) { }
	public VirtualFile(string name, string hash, Lazy<FileInfo> physicalFile)
	{
		this.Name = name;
		this.OriginalHash = hash;
		this.PhysicalFileLazy = physicalFile;
	}

	public VirtualPath GetFullPath()
	{
		if (this.Parent is null)
			return new(this.Name);

		return this.Parent.GetFullPath().WithFileName(this.Name);
	}
}