namespace OsuLazerFSMounter.FileSystem;

public class VirtualFile : IVirtualFileSystemObject
{
	public VirtualDirectory? Parent { get; internal set; }

	public string Name { get; set; }
	public string OriginalHash { get; set; }
	public FileInfo PhysicalFile { get; set; }

	public bool IsNewlyCreated => string.IsNullOrEmpty(this.OriginalHash);

	public VirtualFile(string name, string hash, FileInfo physicalFile)
	{
		this.Name = name;
		this.OriginalHash = hash;
		this.PhysicalFile = physicalFile;
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