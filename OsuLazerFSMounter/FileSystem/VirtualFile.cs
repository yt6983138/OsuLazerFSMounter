using System.Diagnostics;

namespace OsuLazerFSMounter.FileSystem;

[DebuggerDisplay("File {Name}")]
public class VirtualFile : IVirtualFileSystemObject
{
	public VirtualDirectory? Parent { get; internal set; }

	public string Name { get; set; }
	/// <summary>
	/// this indicate the sha256 hash of the file content, it will be updated when Close is called.
	/// Empty if the file is newly created and not yet closed, and original file hash after just loaded from database
	/// </summary>
	public string Hash { get; set; }
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
		this.Hash = hash;
		this.PhysicalFileLazy = physicalFile;
	}

	public VirtualPath GetFullPath()
	{
		if (this.Parent is null)
			return new(this.Name);

		return this.Parent.GetFullPath().WithFileName(this.Name);
	}
}