using System.Diagnostics;

namespace OsuLazerFSMounter.FileSystem;

[DebuggerDisplay("File {Name}")]
public class VirtualFile : IVirtualFileSystemObject
{
	private FileInfo? _cachedPhyiscalFile;

	public VirtualDirectory? Parent { get; internal set; }

	public string Name { get; set; }
	/// <summary>
	/// this indicate the sha256 hash of the file content, it will be updated when Close is called.
	/// Empty if the file is newly created and not yet closed, and original file hash after just loaded from database
	/// </summary>
	public string Hash { get; set; }

	/// <summary>
	/// this property is cached, and will be updated when PhysicalFileLazy is changed or InvalidateCachedPhysicalFile is called
	/// </summary>
	public FileInfo PhysicalFile
	{
		get => this.GetPhysicalFile();
		set => this.PhysicalFileLazy = _ => value;
	}
	public Func<VirtualFile, FileInfo> PhysicalFileLazy
	{
		get => field;
		set
		{
			if (value != field)
			{
				this._cachedPhyiscalFile = null;
				field = value;
			}
		}
	}

	public VirtualFile(string name, string hash, FileInfo physicalFile)
		: this(name, hash, _ => physicalFile) { }
	public VirtualFile(string name, string hash, Func<VirtualFile, FileInfo> physicalFile)
	{
		this.Name = name;
		this.Hash = hash;
		this.PhysicalFileLazy = physicalFile;
	}

	private FileInfo GetPhysicalFile()
	{
		this._cachedPhyiscalFile ??= this.PhysicalFileLazy.Invoke(this);

		return this._cachedPhyiscalFile;
	}

	public void InvalidateCachedPhysicalFile()
	{
		this._cachedPhyiscalFile = null;
	}
	public VirtualPath GetFullPath()
	{
		if (this.Parent is null)
			return new(this.Name);

		return this.Parent.GetFullPath().WithFileName(this.Name);
	}
}