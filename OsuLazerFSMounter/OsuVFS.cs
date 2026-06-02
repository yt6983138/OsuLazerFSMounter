using Fsp;
using Fsp.Interop;
using Microsoft.Extensions.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Skinning;
using OsuLazerFSMounter.FileSystem;
using System.Security.AccessControl;
using FileAttributes_t = System.IO.FileAttributes; // bruh fsp uses pascal case parameter for some reason
using FileInfo = System.IO.FileInfo;
using FSPFileInfo = Fsp.Interop.FileInfo;

namespace OsuLazerFSMounter;

public enum OsuVFSBaseDirectoryType
{
	Songs,
	Skins,
}
public class OsuVFS : FileSystemBase
{
	private record struct RealmFileInfo(string Hash, string Path)
	{
		public RealmFileInfo(RealmNamedFileUsage file)
			: this(file.File.Hash, file.Filename) { }
	}
	private record class PartialBeatmapSetInfo(string Title, Guid ID, int OnlineID, RealmFileInfo[] Files);
	private record class PartialSkinInfo(Guid ID, string Name, RealmFileInfo[] Files);

	private readonly ILogger<OsuVFS> _logger;
	private readonly List<IDescriptor> _openDescriptors = [];
	private readonly ScopedSemaphoreSlim _descriptorLock = new(1, 1);
	private readonly ScopedSemaphoreSlim _rootDirectoryLock = new(1, 1);

	public VirtualDirectory RootDirectory { get; set; } = new("");

	public RealmAccess RealmAccess { get; private init; }
	public DirectoryInfo FilesFolder { get; private init; }
	public string? VolumeLabel { get; set; }

	public OsuVFS(RealmAccess realm, DirectoryInfo hashFileStorage, ILogger<OsuVFS> logger)
	{
		this.RealmAccess = realm;
		this.FilesFolder = hashFileStorage;
		this._logger = logger;
	}

	private static string SanitizeFileName(string name)
	{
		foreach (char c in Path.GetInvalidFileNameChars())
		{
			name = name.Replace(c, '_');
		}
		return name;
	}

	private FileInfo FindPhysical(string hash)
	{
		string first = hash[0].ToString();
		string firstTwo = hash[..2];

		FileInfo info = this.FilesFolder
			.GetDirectories().First(x => x.Name == first)
			.GetDirectories().First(x => x.Name == firstTwo)
			.GetFiles().First(x => x.Name == hash);

		return info;
	}
	public void GetBeatMapDirectories(VirtualDirectory result)
	{
		List<PartialBeatmapSetInfo> files = this.RealmAccess.Run(x => x
			.All<BeatmapSetInfo>()
			.ToArray()
			.Select(x => new PartialBeatmapSetInfo(x.Metadata.Title, x.ID, x.OnlineID, x.Files.Select(y => new RealmFileInfo(y)).ToArray()))
			.ToList());

		foreach (PartialBeatmapSetInfo item in files)
		{
			string name; // the ID hash code is just for anti duplication
			if (item.OnlineID <= 0) name = $"{SanitizeFileName(item.Title)} {item.ID.GetHashCode()}";
			else name = $"{item.OnlineID} {SanitizeFileName(item.Title)}";

			VirtualDirectory directory = result.AddDirectory(VirtualPath.FromDirectory(name));

			foreach (RealmFileInfo file in item.Files)
			{
				VirtualPath path = VirtualPath.FromFile(file.Path);
				directory.AddFile(new(path.FileName, file.Hash, this.FindPhysical(file.Hash)), path);
			}

		}
	}
	public void GetSkinDirectories(VirtualDirectory result)
	{
		List<PartialSkinInfo> files = this.RealmAccess.Run(x => x
			.All<SkinInfo>()
			.ToArray()
			.Select(x => new PartialSkinInfo(x.ID, x.Name, x.Files.Select(y => new RealmFileInfo(y)).ToArray()))
			.ToList());

		foreach (PartialSkinInfo item in files)
		{
			string name = $"{SanitizeFileName(item.Name)} {item.ID.GetHashCode()}";

			VirtualDirectory directory = result.AddDirectory(VirtualPath.FromDirectory(name));

			foreach (RealmFileInfo file in item.Files)
			{
				VirtualPath path = VirtualPath.FromFile(file.Path);
				directory.AddFile(new(path.FileName, file.Hash, this.FindPhysical(file.Hash)), path);
			}
		}
	}

	private FSPFileInfo CreateInfo(FileInfo info)
	{
		info.Refresh(); // make sure the info is up to date
		return new()
		{
			FileSize = (ulong)info.Length,
			AllocationSize = (ulong)info.Length,
			LastAccessTime = (ulong)info.LastAccessTimeUtc.ToFileTimeUtc(),
			LastWriteTime = (ulong)info.LastWriteTimeUtc.ToFileTimeUtc(),
			CreationTime = (ulong)info.CreationTimeUtc.ToFileTimeUtc(),
			ChangeTime = (ulong)info.LastWriteTimeUtc.ToFileTimeUtc()
		};
	}
	private FSPFileInfo CreateInfo(VirtualDirectory dir)
	{
		return new()
		{
			FileAttributes = new FileAttributeBuilder(directory: true).UIntValue,
		};
	}

	private FileStream OpenStreamDefault(FileInfo info, FileMode mode = FileMode.Open, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.ReadWrite | FileShare.Delete)
	{
		return info.Open(mode, access, share);
	}

	public override int ExceptionHandler(Exception ex)
	{
		this._logger.LogError(ex, "Unhandled exception.");
		return base.ExceptionHandler(ex);
	}

	#region Initialization, Finalization
	public override int Init(object Host)
	{
		this._logger.LogInformation("OsuVFS initialized, host: {Host}", Host);

		return STATUS_SUCCESS; // why cant they make it an enum or something?
	}
	public override int Mounted(object Host)
	{
		using ScopedSemaphoreSlim.Scope _ = this._rootDirectoryLock.Enter();

		// TODO: make this faster, for now it takes 5 seconds for me to mount with 1000+ beatmaps and 21 skins, which is pretty bad, maybe we can do some lazy loading or something
		// (maybe related to creating fileinfo)
		VirtualDirectory beatMapDir = this.RootDirectory.AddDirectory(VirtualPath.FromDirectory(nameof(OsuVFSBaseDirectoryType.Songs)));
		VirtualDirectory skinDir = this.RootDirectory.AddDirectory(VirtualPath.FromDirectory(nameof(OsuVFSBaseDirectoryType.Skins)));
		this.GetBeatMapDirectories(beatMapDir);
		this.GetSkinDirectories(skinDir);

		this._logger.LogInformation("OsuVFS mounted, host: {Host}", Host);
		return STATUS_SUCCESS;
	}
	public override void Unmounted(object Host)
	{
		using ScopedSemaphoreSlim.Scope _ = this._rootDirectoryLock.Enter();
		this.RootDirectory.RemoveAll();
		this._logger.LogInformation("OsuVFS unmounted, host: {Host}", Host);
	}
	public override int Open(string FileName, uint CreateOptions, uint GrantedAccess, out object FileNode, out object FileDesc, out FSPFileInfo FileInfo, out string NormalizedName)
	{
		VirtualFile? file = this.RootDirectory.FindFile(VirtualPath.FromFile(FileName));
		VirtualDirectory? dir = this.RootDirectory.FindDirectory(VirtualPath.FromDirectory(FileName));
		// maybe we need a proper way to determine if the path is a file or directory instead 

		using ScopedSemaphoreSlim.Scope _ = this._descriptorLock.Enter();
		if (file is not null)
		{
			this._logger.LogTrace("Open: File {name}", FileName);

			FileDescriptor descriptor = new(file, this.OpenStreamDefault(file.PhysicalFile));
			FileDesc = descriptor;
			FileInfo = this.CreateInfo(file.PhysicalFile);

			this._openDescriptors.Add(descriptor);
		}
		else if (dir is not null)
		{
			this._logger.LogTrace("Open: Directory {name}", FileName);

			DirectoryDescriptor descriptor = new(dir);
			FileDesc = descriptor;
			FileInfo = this.CreateInfo(dir);

			this._openDescriptors.Add(descriptor);
		}
		else
		{
			this._logger.LogWarning("Open: Not found {name}", FileName);

			FileNode = null!;
			FileDesc = null!;
			FileInfo = default;
			NormalizedName = null!;
			return STATUS_OBJECT_NAME_NOT_FOUND;
		}

		NormalizedName = null!;
		FileNode = null!;

		return STATUS_SUCCESS;
	}
	public override int Flush(object FileNode, object FileDesc, out FSPFileInfo FileInfo)
	{
		this._logger.LogDebug("Flushed: {FileDesc}", FileDesc);

		if (FileDesc is not FileDescriptor node)
		{
			FileInfo = default;
			return STATUS_INVALID_PARAMETER;
		}

		node.Stream.Flush();
		FileInfo = this.CreateInfo(node.File.PhysicalFile) with
		{
			FileSize = (ulong)node.Stream.Length,
			AllocationSize = (ulong)node.Stream.Length,
		};

		return STATUS_SUCCESS;
	}
	public override void Close(object FileNode, object FileDesc)
	{
		if (FileDesc is not IDescriptor descriptor)
		{
			this._logger.LogWarning("Close called with invalid FileDesc: {FileDesc}", FileDesc);
			return;
		}

		this._logger.LogTrace("Closed: {FileDesc}", FileDesc);

		if (FileDesc is FileDescriptor node)
		{
			// TODO: implement update file hash etc
		}

		descriptor.Dispose();
		using ScopedSemaphoreSlim.Scope _ = this._descriptorLock.Enter();
		this._openDescriptors.Remove(descriptor);
	}
	public override void Cleanup(object FileNode, object FileDesc, string FileName, uint Flags)
	{
		// TODO: figure out if something is needed here
		// TODO: delete
		this._logger.LogTrace("Cleanup: {FileDesc}", FileDesc);
		base.Cleanup(FileNode, FileDesc, FileName, Flags);
	}
	#endregion

	#region Read
	public override unsafe int Read(object FileNode, object FileDesc, nint Buffer, ulong Offset, uint Length, out uint BytesTransferred)
	{
		this._logger.LogTrace("Read: {FileDesc}", FileDesc);

		if (FileDesc is not FileDescriptor node)
		{
			BytesTransferred = 0;
			return STATUS_INVALID_PARAMETER;
		}

		using ScopedSemaphoreSlim.Scope _ = node.Lock.Enter();
		node.Stream.Seek((long)Offset, SeekOrigin.Begin);

		Span<byte> span = new(Buffer.ToPointer(), (int)Length);
		BytesTransferred = (uint)node.Stream.Read(span);

		return STATUS_SUCCESS;
	}

	public override int ReadDirectory(object FileNode, object FileDesc, string Pattern, string Marker, nint Buffer, uint Length, out uint BytesTransferred)
	{
		// left as-is, it has default implementation that uses ReadDirectoryEntry
		this._logger.LogTrace("ReadDirectory: {FileDesc}, Pattern: {Pattern}, Marker: {Marker}", FileDesc, Pattern, Marker);

		if (FileDesc is not IDescriptor descriptor)
		{
			BytesTransferred = 0;
			return STATUS_INVALID_PARAMETER;
		}

		using ScopedSemaphoreSlim.Scope _ = descriptor.Lock.Enter();
		return base.ReadDirectory(FileNode, FileDesc, Pattern, Marker, Buffer, Length, out BytesTransferred);
	}
	// return: current is valid, next may not be valid
	public override bool ReadDirectoryEntry(object FileNode, object FileDesc, string Pattern, string Marker, ref object Context, out string FileName, out FSPFileInfo FileInfo)
	{
		if (FileDesc is not DirectoryDescriptor node)
		{
			FileInfo = default;
			FileName = null!;
			return false;
		}

		if (Context is not ReadDirectoryContext context)
		{
			context = new ReadDirectoryContext(node.Directory)
			{
				LastPosition = -1,
			};
			Context = context;
		}

		bool hasReachedMarker = Marker is null;
	LoopToMarker:
		context.LastPosition++;

		if (context.LastPosition >= context.Directory.Files.Count + context.Directory.Subdirectories.Count)
		{
			FileInfo = default;
			FileName = null!;
			Context = null!;
			return false;
		}

		if (context.LastPosition >= context.Directory.Subdirectories.Count)
		{
			int filePos = context.LastPosition - context.Directory.Subdirectories.Count;

			VirtualFile file = context.Directory.Files[filePos];
			if (!hasReachedMarker)
			{
				hasReachedMarker = file.Name == Marker;
				goto LoopToMarker;
			}

			FileName = file.Name;
			FileInfo = this.CreateInfo(file.PhysicalFile);
			return true;
		}
		else
		{
			VirtualDirectory subdir = context.Directory.Subdirectories[context.LastPosition];
			if (!hasReachedMarker)
			{
				hasReachedMarker = subdir.Name == Marker;
				goto LoopToMarker;
			}

			FileName = subdir.Name;
			FileInfo = this.CreateInfo(subdir);
			return true;
		}
	}

	public override int GetDirInfoByName(object FileNode, object FileDesc, string FileName, out string NormalizedName, out FSPFileInfo FileInfo)
	{
		// not implemented
		return base.GetDirInfoByName(FileNode, FileDesc, FileName, out NormalizedName, out FileInfo);
	}
	public override int GetFileInfo(object FileNode, object FileDesc, out FSPFileInfo FileInfo)
	{
		if (FileDesc is FileDescriptor node)
		{
			FileInfo = this.CreateInfo(node.File.PhysicalFile);
			return STATUS_SUCCESS;
		}
		else if (FileDesc is DirectoryDescriptor dirNode)
		{
			FileInfo = this.CreateInfo(dirNode.Directory);
			return STATUS_SUCCESS;
		}

		FileInfo = default;
		return STATUS_INVALID_PARAMETER;
	}

	public override int GetReparsePoint(object FileNode, object FileDesc, string FileName, ref byte[] ReparseData)
	{
		// not implemented
		return base.GetReparsePoint(FileNode, FileDesc, FileName, ref ReparseData);
	}
	public override int GetReparsePointByName(string FileName, bool IsDirectory, ref byte[] ReparseData)
	{
		// not implemented
		return base.GetReparsePointByName(FileName, IsDirectory, ref ReparseData);
	}

	public override int GetSecurity(object FileNode, object FileDesc, ref byte[] SecurityDescriptor)
	{
		// not implemented, security is not really that important in this case (prob will be a huge hole lmao)
		return base.GetSecurity(FileNode, FileDesc, ref SecurityDescriptor);
	}
	public override int GetSecurityByName(string FileName, out uint FileAttributes, ref byte[] SecurityDescriptor)
	{
		this._logger.LogTrace("GetSecurityByName: {name}", FileName);
		VirtualDirectory? dir = this.RootDirectory.FindDirectory(VirtualPath.FromDirectory(FileName));
		VirtualFile? file = this.RootDirectory.FindFile(VirtualPath.FromFile(FileName));
		if (file is not null)
		{
			FileAttributes = new FileAttributeBuilder().UIntValue;
			return STATUS_SUCCESS;
		}
		else if (dir is not null)
		{
			FileAttributes = new FileAttributeBuilder(directory: true).UIntValue;
			return STATUS_SUCCESS;
		}

		FileAttributes = 0;
		return STATUS_OBJECT_NAME_NOT_FOUND;
	}

	public override bool GetStreamEntry(object FileNode, object FileDesc, ref object Context, out string StreamName, out ulong StreamSize, out ulong StreamAllocationSize)
	{
		// not implemented
		return base.GetStreamEntry(FileNode, FileDesc, ref Context, out StreamName, out StreamSize, out StreamAllocationSize);
	}

	public override int GetVolumeInfo(out VolumeInfo VolumeInfo)
	{
		VolumeInfo = new()
		{
			FreeSize = 0,
			TotalSize = 0,
		};
		VolumeInfo.SetVolumeLabel(this.VolumeLabel);
		return STATUS_SUCCESS;
	}

	public override bool GetEaEntry(object FileNode, object FileDesc, ref object Context, out string EaName, out byte[] EaValue, out bool NeedEa)
	{
		// not implemented
		return base.GetEaEntry(FileNode, FileDesc, ref Context, out EaName, out EaValue, out NeedEa);
	}
	#endregion

	#region Write
	public override unsafe int Write(object FileNode, object FileDesc, nint Buffer, ulong Offset, uint Length, bool WriteToEndOfFile, bool ConstrainedIo, out uint BytesTransferred, out FSPFileInfo FileInfo)
	{
		this._logger.LogDebug("Write: {FileDesc}", FileDesc);

		if (FileDesc is not FileDescriptor node)
		{
			BytesTransferred = 0;
			FileInfo = default;
			return STATUS_INVALID_PARAMETER;
		}

		using ScopedSemaphoreSlim.Scope _ = node.Lock.Enter();
		Span<byte> span = new(Buffer.ToPointer(), (int)Length);
		node.Stream.Seek((long)Offset, SeekOrigin.Begin);
		node.Stream.Write(span);
		BytesTransferred = Length;
		FileInfo = this.CreateInfo(node.File.PhysicalFile) with
		{
			AllocationSize = (ulong)node.Stream.Length,
			FileSize = (ulong)node.Stream.Length
		};
		return STATUS_SUCCESS;
	}
	public override int Overwrite(object FileNode, object FileDesc, uint FileAttributes, bool ReplaceFileAttributes, ulong AllocationSize, out FSPFileInfo FileInfo)
	{
		this._logger.LogDebug("Overwrite: {FileDesc}", FileDesc);

		if (FileDesc is not FileDescriptor node)
		{
			FileInfo = default;
			return STATUS_INVALID_PARAMETER;
		}

		using ScopedSemaphoreSlim.Scope _ = node.Lock.Enter();
		node.Stream.Dispose();
		node.Stream = this.OpenStreamDefault(node.File.PhysicalFile, FileMode.Create);
		FileInfo = this.CreateInfo(node.File.PhysicalFile) with
		{
			FileSize = 0,
			AllocationSize = AllocationSize, // not sure if this will work
		};
		return STATUS_SUCCESS;
	}

	public override int SetBasicInfo(object FileNode, object FileDesc, uint FileAttributes, ulong CreationTime, ulong LastAccessTime, ulong LastWriteTime, ulong ChangeTime, out FSPFileInfo FileInfo)
	{
		// not implemented
		return base.SetBasicInfo(FileNode, FileDesc, FileAttributes, CreationTime, LastAccessTime, LastWriteTime, ChangeTime, out FileInfo);
	}
	public override int SetFileSize(object FileNode, object FileDesc, ulong NewSize, bool SetAllocationSize, out FSPFileInfo FileInfo)
	{
		this._logger.LogDebug("SetFileSize: {FileDesc}, {size}", FileDesc, NewSize);

		if (FileDesc is not FileDescriptor node)
		{
			FileInfo = default;
			return STATUS_INVALID_PARAMETER;
		}

		if (SetAllocationSize)
		{
			// basically do nothing
			FileInfo = this.CreateInfo(node.File.PhysicalFile) with
			{
				FileSize = (ulong)node.Stream.Length,
				AllocationSize = NewSize
			};
			return STATUS_SUCCESS;
		}

		using ScopedSemaphoreSlim.Scope _ = node.Lock.Enter();
		node.Stream.SetLength((long)NewSize);
		FileInfo = this.CreateInfo(node.File.PhysicalFile) with
		{
			FileSize = NewSize
		};
		return STATUS_SUCCESS;
	}
	public override int SetReparsePoint(object FileNode, object FileDesc, string FileName, byte[] ReparseData)
	{
		// not implemented
		return base.SetReparsePoint(FileNode, FileDesc, FileName, ReparseData);
	}
	public override int SetSecurity(object FileNode, object FileDesc, AccessControlSections Sections, byte[] SecurityDescriptor)
	{
		// not implemented, security is not really that important in this case (prob will be a huge hole lmao)
		return base.SetSecurity(FileNode, FileDesc, Sections, SecurityDescriptor);
	}
	public override int SetVolumeLabel(string VolumeLabel, out VolumeInfo VolumeInfo)
	{
		// not implemented
		return base.SetVolumeLabel(VolumeLabel, out VolumeInfo);
	}

	public override int SetEaEntry(object FileNode, object FileDesc, ref object Context, string EaName, byte[] EaValue, bool NeedEa)
	{
		// not implemented
		return base.SetEaEntry(FileNode, FileDesc, ref Context, EaName, EaValue, NeedEa);
	}
	#endregion

	#region Create, Delete, Rename (Only one operation at same time)
	private bool IsValidWriteOperation(string fileName)
	{
		string[] paths = VirtualPath.BreakIntoDirectoryPathsAndSanitize(fileName);
		if (paths.Length < 3)
		{
			// only allows creating both folders and files in song-specific or skin-specific folder
			return false;
		}

		return true;
	}

	public override int Create(string FileName, uint CreateOptions, uint GrantedAccess, uint FileAttributes, byte[] SecurityDescriptor, ulong AllocationSize, out object FileNode, out object FileDesc, out FSPFileInfo FileInfo, out string NormalizedName)
	{
		this._logger.LogDebug("Create: {name}, {attr}", FileName, (FileAttributes_t)FileAttributes);

		if (!this.IsValidWriteOperation(FileName))
		{
			FileNode = null!;
			FileDesc = null!;
			FileInfo = default;
			NormalizedName = null!;
			return STATUS_NOT_SUPPORTED;
		}

		// note ignoring file attributes, granted access, create options, security descriptor etc for now, will implement later if needed
		if (FileAttributes.HasFlag((uint)FileAttributes_t.Directory))
		{
			if (this.RootDirectory.FindDirectory(VirtualPath.FromDirectory(FileName)) is not null)
			{
				FileNode = null!;
				FileDesc = null!;
				FileInfo = default;
				NormalizedName = null!;
				return STATUS_OBJECT_NAME_COLLISION;
			}

			VirtualDirectory dir = this.RootDirectory.AddDirectory(VirtualPath.FromDirectory(FileName));

			FileNode = null!;
			FileDesc = new DirectoryDescriptor(dir);
			FileInfo = this.CreateInfo(dir);
			NormalizedName = null!;
			return STATUS_SUCCESS;
		}
		else
		{
			if (this.RootDirectory.FindFile(VirtualPath.FromFile(FileName)) is not null)
			{
				FileNode = null!;
				FileDesc = null!;
				FileInfo = default;
				NormalizedName = null!;
				return STATUS_OBJECT_NAME_COLLISION;
			}

			VirtualPath filePath = VirtualPath.FromFile(FileName);
			FileInfo physicalFile = new(Path.GetTempFileName());
			VirtualFile file = new(filePath.FileName, "", physicalFile);
			this.RootDirectory.AddFile(file, filePath);

			FileNode = null!;
			FileDesc = new FileDescriptor(file, this.OpenStreamDefault(physicalFile));
			FileInfo = this.CreateInfo(physicalFile);
			NormalizedName = null!;
			return STATUS_SUCCESS;
		}
	}
	public override int Rename(object FileNode, object FileDesc, string FileName, string NewFileName, bool ReplaceIfExists)
	{
		this._logger.LogDebug("Rename: [overwrite: {ow}] {old} to {new}", ReplaceIfExists, FileName, NewFileName);

		if (!this.IsValidWriteOperation(NewFileName) || !this.IsValidWriteOperation(FileName))
		{
			return STATUS_NOT_SUPPORTED;
		}

		if (FileDesc is DirectoryDescriptor dirNode)
		{
			using ScopedSemaphoreSlim.Scope _ = dirNode.Lock.Enter();

			VirtualPath oldDirPath = VirtualPath.FromDirectory(FileName);
			VirtualPath newDirPath = VirtualPath.FromDirectory(NewFileName);

			VirtualDirectory? targetDir = this.RootDirectory.FindDirectory(newDirPath);
			if (targetDir is not null)
			{
				return STATUS_OBJECT_NAME_COLLISION;
			}

			VirtualDirectory? oldParent = this.RootDirectory.FindDirectory(oldDirPath)?.Parent;
			if (oldParent is null)
			{
				this._logger.LogWarning("Failed to find parent directory for {FileName} during rename operation", FileName);
				return STATUS_UNEXPECTED_IO_ERROR;
			}

			VirtualDirectory oldDir = dirNode.Directory;
			this.RootDirectory.RemoveDirectory(oldDirPath);
			this.RootDirectory.AddDirectory(oldDir, newDirPath.Mutate(x => x[..^1]));
			oldDir.Name = newDirPath.DirectorySegments.Last();

			return STATUS_SUCCESS;
		}
		else if (FileDesc is FileDescriptor node)
		{
			using ScopedSemaphoreSlim.Scope _ = node.Lock.Enter();

			VirtualPath oldPath = VirtualPath.FromFile(FileName);
			VirtualPath newPath = VirtualPath.FromFile(NewFileName);

			VirtualFile? targetFile = this.RootDirectory.FindFile(newPath);

			VirtualDirectory? newParent = targetFile?.Parent;
			VirtualDirectory? oldParent = node.File.Parent;

			if (oldParent is null)
			{
				this._logger.LogWarning("Failed to find parent directory for {FileName} during rename operation", FileName);
				return STATUS_UNEXPECTED_IO_ERROR;
			}

			if (targetFile is not null)
			{
				if (!ReplaceIfExists)
				{
					return STATUS_OBJECT_NAME_COLLISION;
				}
				this.RootDirectory.RemoveFile(newPath);
			}

			this.RootDirectory.RemoveFile(oldPath);

			node.File = new(newPath.FileName, node.File.OriginalHash, node.File.PhysicalFile);
			this.RootDirectory.AddFile(node.File, newPath);

			return STATUS_SUCCESS;
		}

		return STATUS_INVALID_PARAMETER;
	}
	public override int CanDelete(object FileNode, object FileDesc, string FileName)
	{
		if (!this.IsValidWriteOperation(FileName))
		{
			return STATUS_NOT_SUPPORTED;
		}

		// TODO: actually implement this
		if (FileDesc is DirectoryDescriptor dirNode)
		{
			if (dirNode.Directory.Subdirectories.Count > 0 || dirNode.Directory.Files.Count > 0)
			{
				return STATUS_DIRECTORY_NOT_EMPTY;
			}
		}
		else if (FileDesc is FileDescriptor)
		{
			// no checks for now
			return STATUS_SUCCESS;
		}

		return STATUS_INVALID_PARAMETER;
	}
	public override int DeleteReparsePoint(object FileNode, object FileDesc, string FileName, byte[] ReparseData)
	{
		// not implemented
		return base.DeleteReparsePoint(FileNode, FileDesc, FileName, ReparseData);
	}
	public override int SetDelete(object FileNode, object FileDesc, string FileName, bool DeleteFile)
	{
		// not implemented
		return base.SetDelete(FileNode, FileDesc, FileName, DeleteFile);
	}
	#endregion

	#region Miscellaneous
	public override int Control(object FileNode, object FileDesc, uint ControlCode, nint InputBuffer, uint InputBufferLength, nint OutputBuffer, uint OutputBufferLength, out uint BytesTransferred)
	{
		// not implemented
		return base.Control(FileNode, FileDesc, ControlCode, InputBuffer, InputBufferLength, OutputBuffer, OutputBufferLength, out BytesTransferred);
	}
	#endregion
}
