using Fsp;
using Fsp.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Skinning;
using OsuLazerFSMounter.FileSystem;
using System.Security.AccessControl;
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

	public List<VirtualDirectory> GetBeatMapDirectories()
	{
		List<PartialBeatmapSetInfo> files = this.RealmAccess.Run(x => x
			.All<BeatmapSetInfo>()
			.ToArray()
			.Select(x => new PartialBeatmapSetInfo(x.Metadata.Title, x.ID, x.OnlineID, x.Files.Select(y => new RealmFileInfo(y)).ToArray()))
			.ToList());

		List<VirtualDirectory> result = [];
		foreach (PartialBeatmapSetInfo item in files)
		{
			string name; // the ID hash code is just for anti duplication
			if (item.OnlineID <= 0) name = $"{SanitizeFileName(item.Title)} {item.ID.GetHashCode()}";
			else name = $"{item.OnlineID} {SanitizeFileName(item.Title)}";

			VirtualDirectory directory = new(name);

			foreach (RealmFileInfo file in item.Files)
			{
				directory.AddFile(file.Path, file.Hash);
			}

			result.Add(directory);
		}

		return result;
	}
	public List<VirtualDirectory> GetSkinDirectories()
	{
		List<PartialSkinInfo> files = this.RealmAccess.Run(x => x
			.All<SkinInfo>()
			.ToArray()
			.Select(x => new PartialSkinInfo(x.ID, x.Name, x.Files.Select(y => new RealmFileInfo(y)).ToArray()))
			.ToList());

		List<VirtualDirectory> result = [];
		foreach (PartialSkinInfo item in files)
		{
			string name = $"{SanitizeFileName(item.Name)} {item.ID.GetHashCode()}";

			VirtualDirectory directory = new(name);

			foreach (RealmFileInfo file in item.Files)
			{
				directory.AddFile(file.Path, file.Hash);
			}

			result.Add(directory);
		}

		return result;
	}

	private FSPFileInfo CreateInfo(FileInfo info)
	{
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
		this.RootDirectory.Subdirectories.Add(new(nameof(OsuVFSBaseDirectoryType.Songs)) { Subdirectories = this.GetBeatMapDirectories() });
		this.RootDirectory.Subdirectories.Add(new(nameof(OsuVFSBaseDirectoryType.Skins)) { Subdirectories = this.GetSkinDirectories() });
		this._logger.LogInformation("OsuVFS mounted, host: {Host}", Host);
		return STATUS_SUCCESS;
	}
	public override void Unmounted(object Host)
	{
		this.RootDirectory.Subdirectories.Clear();
		this.RootDirectory.Files.Clear();
		this._logger.LogInformation("OsuVFS unmounted, host: {Host}", Host);
	}

	private FileInfo FindPhysical(VirtualFile file)
	{
		string first = file.Hash[0].ToString();
		string firstTwo = file.Hash[..2];

		FileInfo info = this.FilesFolder
			.GetDirectories().First(x => x.Name == first)
			.GetDirectories().First(x => x.Name == firstTwo)
			.GetFiles().First(x => x.Name == file.Hash);

		return info;
	}
	public override int Open(string fileName, uint createOptions, uint grantedAccess, out object fileNode, out object fileDesc, out FSPFileInfo fileInfo, out string NormalizedName)
	{
		string[] brokenPath = VirtualDirectory.BreakIntoDirectoryPathsAndSanitize(fileName);
		VirtualFile? file = this.RootDirectory.FindFile(fileName);
		VirtualDirectory? dir = this.RootDirectory.FindDirectory(fileName);

		if (file is not null)
		{
			FileInfo physicalFile = this.FindPhysical(file);

			fileDesc = new FileNode(brokenPath, file, this.OpenStreamDefault(physicalFile), physicalFile);
			fileInfo = this.CreateInfo(physicalFile);
		}
		else if (dir is not null)
		{
			fileDesc = new DirectoryNode(VirtualDirectory.EnsureLastIsDirectory(brokenPath), dir);
			fileInfo = this.CreateInfo(dir);
		}
		else
		{
			fileNode = null!;
			fileDesc = null!;
			fileInfo = default;
			NormalizedName = null!;
			return STATUS_OBJECT_NAME_NOT_FOUND;
		}

		NormalizedName = null!;
		fileNode = null!;

		return STATUS_SUCCESS;
	}
	public override int Flush(object FileNode, object FileDesc, out FSPFileInfo FileInfo)
	{
		if (FileDesc is not FileNode node)
		{
			FileInfo = default;
			return STATUS_INVALID_PARAMETER;
		}

		node.Stream.Flush();
		FileInfo = this.CreateInfo(node.Info) with
		{
			FileSize = (ulong)node.Stream.Length,
			AllocationSize = (ulong)node.Stream.Length,
		};

		return STATUS_SUCCESS;
	}
	public override void Close(object FileNode, object FileDesc)
	{
		if (FileDesc is not FileNode node)
		{
			this._logger.LogWarning("Close called with invalid FileDesc: {FileDesc}", FileDesc);
			return;
		}

		node.Stream.Dispose();
		// TODO: implement update file hash etc
	}
	public override void Cleanup(object FileNode, object FileDesc, string FileName, uint Flags)
	{
		// TODO: figure out if something is needed here
		// TODO: delete
		base.Cleanup(FileNode, FileDesc, FileName, Flags);
	}
	#endregion

	#region Read
	public override unsafe int Read(object FileNode, object FileDesc, nint Buffer, ulong Offset, uint Length, out uint BytesTransferred)
	{
		if (FileDesc is not FileNode node)
		{
			BytesTransferred = 0;
			return STATUS_INVALID_PARAMETER;
		}
		node.Stream.Seek((long)Offset, SeekOrigin.Begin);

		Span<byte> span = new(Buffer.ToPointer(), (int)Length);
		BytesTransferred = (uint)node.Stream.Read(span);

		return STATUS_SUCCESS;
	}

	public override int ReadDirectory(object FileNode, object FileDesc, string Pattern, string Marker, nint Buffer, uint Length, out uint BytesTransferred)
	{
		// left as-is
		return base.ReadDirectory(FileNode, FileDesc, Pattern, Marker, Buffer, Length, out BytesTransferred);
	}
	// return: current is valid, next may not be valid
	public override bool ReadDirectoryEntry(object FileNode, object FileDesc, string Pattern, string Marker, ref object Context, out string FileName, out FSPFileInfo FileInfo)
	{
		if (FileDesc is not DirectoryNode node)
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
	Loop:
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
				goto Loop;
			}

			FileInfo info = this.FindPhysical(file);

			FileName = file.Name;
			FileInfo = this.CreateInfo(info);
			return true;
		}
		else
		{
			VirtualDirectory subdir = context.Directory.Subdirectories[context.LastPosition];
			if (!hasReachedMarker)
			{
				hasReachedMarker = subdir.Name == Marker;
				goto Loop;
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
		if (FileDesc is FileNode node)
		{
			FileInfo = this.CreateInfo(node.Info);
			return STATUS_SUCCESS;
		}
		else if (FileDesc is DirectoryNode dirNode)
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
		VirtualFile? file = this.RootDirectory.FindFile(FileName);
		VirtualDirectory? dir = this.RootDirectory.FindDirectory(FileName);
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
		if (FileDesc is not FileNode node)
		{
			BytesTransferred = 0;
			FileInfo = default;
			return STATUS_INVALID_PARAMETER;
		}
		Span<byte> span = new(Buffer.ToPointer(), (int)Length);
		node.Stream.Seek((long)Offset, SeekOrigin.Begin);
		node.Stream.Write(span);
		BytesTransferred = Length;
		FileInfo = this.CreateInfo(node.Info) with
		{
			AllocationSize = (ulong)node.Stream.Length,
			FileSize = (ulong)node.Stream.Length
		};
		return STATUS_SUCCESS;
	}
	public override int Overwrite(object FileNode, object FileDesc, uint FileAttributes, bool ReplaceFileAttributes, ulong AllocationSize, out FSPFileInfo FileInfo)
	{
		if (FileDesc is not FileNode node)
		{
			FileInfo = default;
			return STATUS_INVALID_PARAMETER;
		}

		node.Stream.Dispose();
		node.Stream = this.OpenStreamDefault(node.Info, FileMode.Create);
		FileInfo = this.CreateInfo(node.Info) with
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
		if (FileDesc is not FileNode node)
		{
			FileInfo = default;
			return STATUS_INVALID_PARAMETER;
		}

		node.Stream.SetLength((long)NewSize);
		FileInfo = this.CreateInfo(node.Info) with
		{
			FileSize = NewSize,
			AllocationSize = SetAllocationSize ? NewSize : (ulong)node.Stream.Length
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

	#region Create, Delete, Rename
	private bool IsValidWriteOperation(string fileName)
	{
		string[] paths = VirtualDirectory.BreakIntoDirectoryPathsAndSanitize(fileName);
		if (paths.Length < 3)
		{
			// only allows creating both folders and files in song-specific or skin-specific folder
			return false;
		}

		return true;
	}

	public override int Create(string FileName, uint CreateOptions, uint GrantedAccess, uint FileAttributes, byte[] SecurityDescriptor, ulong AllocationSize, out object FileNode, out object FileDesc, out FSPFileInfo FileInfo, out string NormalizedName)
	{
		if (!this.IsValidWriteOperation(FileName))
		{
			FileNode = null!;
			FileDesc = null!;
			FileInfo = default;
			NormalizedName = null!;
			return STATUS_NOT_SUPPORTED;
		}

		// note ignoring file attributes, granted access, create options, security descriptor etc for now, will implement later if needed
		if (FileAttributes.HasFlag((uint)FileAttribute.Directory))
		{
			if (this.RootDirectory.FindDirectory(FileName) is not null)
				goto Collision;

			VirtualDirectory dir = this.RootDirectory.AddDirectory(FileName);

			FileNode = null!;
			FileDesc = new DirectoryNode(VirtualDirectory.BreakIntoDirectoryPathsAndSanitize(FileName), dir);
			FileInfo = this.CreateInfo(dir);
			NormalizedName = null!;
			return STATUS_SUCCESS;
		}
		else
		{
			if (this.RootDirectory.FindFile(FileName) is not null)
				goto Collision;

			VirtualFile file = this.RootDirectory.AddFile(FileName, ""); // add with empty hash, will be updated later when the file is closed
			FileInfo physicalFile = new(Path.GetTempFileName());

			FileNode = null!;
			FileDesc = new FileNode(VirtualDirectory.BreakIntoDirectoryPathsAndSanitize(FileName), file, this.OpenStreamDefault(physicalFile), physicalFile);
			FileInfo = this.CreateInfo(physicalFile);
			NormalizedName = null!;
			return STATUS_SUCCESS;
		}

	Collision:
		FileNode = null!;
		FileDesc = null!;
		FileInfo = default;
		NormalizedName = null!;
		return STATUS_OBJECT_NAME_COLLISION;
	}
	public override int Rename(object FileNode, object FileDesc, string FileName, string NewFileName, bool ReplaceIfExists)
	{
		if (!this.IsValidWriteOperation(NewFileName) || !this.IsValidWriteOperation(FileName))
		{
			return STATUS_NOT_SUPPORTED;
		}

		if (FileDesc is DirectoryNode dirNode)
		{
			VirtualDirectory? targetDir = this.RootDirectory.FindDirectory(FileName);
			if (targetDir is not null)
			{
				return STATUS_OBJECT_NAME_COLLISION;
			}

			VirtualDirectory? oldParent = this.RootDirectory.GetPathParent(FileName);
			if (oldParent is null)
			{
				this._logger.LogWarning("Failed to find parent directory for {FileName} during rename operation", FileName);
				return STATUS_UNEXPECTED_IO_ERROR;
			}
			oldParent.Subdirectories.Remove(dirNode.Directory);
			VirtualDirectory oldNode = dirNode.Directory;

			dirNode.SplitPath = VirtualDirectory.BreakIntoDirectoryPathsAndSanitize(NewFileName);
			dirNode.Directory = this.RootDirectory.AddDirectory(NewFileName);
			dirNode.Directory.Subdirectories = oldNode.Subdirectories;
			dirNode.Directory.Files = oldNode.Files;

			return STATUS_SUCCESS;
		}
		else if (FileDesc is FileNode node)
		{
			VirtualFile? targetFile = this.RootDirectory.FindFile(NewFileName);
			VirtualDirectory? newParent = this.RootDirectory.GetPathParent(NewFileName);
			if (targetFile is not null)
			{
				if (!ReplaceIfExists)
					return STATUS_OBJECT_NAME_COLLISION;

				VirtualDirectory? oldParent = this.RootDirectory.GetPathParent(FileName);
				if (oldParent is null)
				{
					this._logger.LogWarning("Failed to find parent directory for {FileName} during rename operation", FileName);
					return STATUS_UNEXPECTED_IO_ERROR;
				}

				targetFile.Hash = node.File.Hash;
				// setting the file simply because it will be updated in close or cleanup anyway
				node.File = targetFile;
				node.SplitPath = VirtualDirectory.BreakIntoDirectoryPathsAndSanitize(NewFileName);

				return STATUS_SUCCESS;
			}

			VirtualFile newNode = this.RootDirectory.AddFile(NewFileName, node.File.Hash);
			node.File = newNode;
			node.SplitPath = VirtualDirectory.BreakIntoDirectoryPathsAndSanitize(NewFileName);
		}

		return STATUS_INVALID_PARAMETER;
	}
	public override int CanDelete(object FileNode, object FileDesc, string FileName)
	{
		if (!this.IsValidWriteOperation(FileName))
		{
			return STATUS_NOT_SUPPORTED;
		}

		if (FileDesc is DirectoryNode dirNode)
		{
			if (dirNode.Directory.Subdirectories.Count > 0 || dirNode.Directory.Files.Count > 0)
			{
				return STATUS_DIRECTORY_NOT_EMPTY;
			}
		}
		else if (FileDesc is FileNode)
		{
			return STATUS_SUCCESS; // no checks for now
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
