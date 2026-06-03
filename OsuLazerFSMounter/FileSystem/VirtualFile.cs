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

	public bool IsNewlyCreated => string.IsNullOrEmpty(this.OriginalHash);

	public VirtualFile(string name, string hash, FileInfo physicalFile)
		: this(name, hash, new Lazy<FileInfo>(physicalFile)) { }
	public VirtualFile(string name, string hash, Lazy<FileInfo> physicalFile)
	{
		this.Name = name;
		this.OriginalHash = hash;
		this.PhysicalFileLazy = physicalFile;
	}

	/// <summary>
	/// this locks the file and all of its parent directories, and returns the full path of this file
	/// </summary>
	/// <returns></returns>
	public VirtualPath GetFullPath()
	{
		if (this.Parent is null)
			return new(this.Name);

		return this.Parent.GetFullPath().WithFileName(this.Name);
	}
}