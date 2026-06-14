using Fsp;
using Fsp.Interop;
using Microsoft.Extensions.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Skinning;
using OsuLazerFSMounter.FileSystem;
using Realms;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Security.Cryptography;
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
	private record struct RealmFlattenedFileInfo(VirtualPath Path, VirtualFile File);
	private record struct RealmFileInfo(string Hash, string Path)
	{
		public RealmFileInfo(RealmNamedFileUsage file)
			: this(file.File.Hash, file.Filename) { }
	}
	private record class PartialBeatmapSetInfo(string Title, Guid ID, int OnlineID, RealmFileInfo[] Files);
	private record class PartialSkinInfo(Guid ID, string Name, RealmFileInfo[] Files);
	private record class ReverseHashInfo(List<VirtualFile> Files)
	{
		public ScopedSemaphoreSlim Lock { get; } = new(1, 1);
	}

	private readonly ILogger<OsuVFS> _logger;
	private readonly FrozenDictionary<string, DirectoryInfo> _cachedHashDirectories;
	/// <summary>
	/// this is only for storing files that have the same hash, so they will point to the same physical file, 
	/// this dictionary may be modified and kinda works as a temporary database, so it will not hold all files
	/// </summary>
	private readonly ConcurrentDictionary<string, ReverseHashInfo> _reverseHashFileDictionary = new();
	private readonly DirectoryInfo _tempFolder = Directory.CreateTempSubdirectory("osu_vfs_");

	public VirtualDirectory RootDirectory { get; set; } = new("");
	public ScopedSemaphoreSlim RootDirectoryLock { get; } = new(1, 1);

	public RealmAccess RealmAccess { get; private init; }
	public DirectoryInfo FilesFolder { get; private init; }
	public OsuVFSOption Option { get; private init; }

	public OsuVFS(RealmAccess realm, DirectoryInfo hashFileStorage, ILogger<OsuVFS> logger, OsuVFSOption option)
	{
		this.RealmAccess = realm;
		this.FilesFolder = hashFileStorage;
		this._logger = logger;
		this._cachedHashDirectories = hashFileStorage.GetDirectories()
			.Where(x => x.Name.Length == 1 && char.IsAsciiHexDigitLower(x.Name[0]))
			.SelectMany(y => y.GetDirectories().Where(x => x.Name.Length == 2 && x.Name.All(char.IsAsciiHexDigitLower)))
			.ToFrozenDictionary(x => x.Name, x => x);
		this.Option = option;
	}

	#region Realm related
	private static string SanitizeFileName(string name)
	{
		if (name == "." || name == "..")
			return $"_{name}";

		string illegalChars = "\"*/:<>?\\|\0";

		char[] newName = name.ToArray();
		foreach (char item in illegalChars)
		{
			newName.Replace(item, '_');
		}
		for (int i = 0; i < newName.Length; i++)
		{
			if (char.IsControl(newName[i])) newName[i] = '_';
		}

		return new(newName);
	}

	private void AddFileToDirectoryAndReverseHash(VirtualDirectory dir, RealmFileInfo file)
	{
		VirtualPath path = VirtualPath.FromFile(file.Path);

		// quite hacky, maybe i should redesign this?
		VirtualFile virtualFile = new(path.FileName, file.Hash, (Lazy<FileInfo>)null!);
		virtualFile.PhysicalFileLazy = new(() => this.LazyFetchFileInfo(virtualFile));

		dir.AddFile(virtualFile, path);
		this._reverseHashFileDictionary.AddOrUpdate(
			file.Hash,
			_ => new([virtualFile]),
			(_, info) =>
			{
				using ScopedSemaphoreSlim.Scope _2 = info.Lock.Enter();
				info.Files.Add(virtualFile);
				return info;
			});
	}

	private FileInfo LazyFetchFileInfo(VirtualFile self)
	{
		FileInfo? physical = this._cachedHashDirectories[self.OriginalHash[0..2]]
			.EnumerateFiles(self.OriginalHash)
			.FirstOrDefault(x => x.Name == self.OriginalHash);

		if (physical is null)
		{
			this._logger.LogWarning("Failed to find physical file for hash {hash}, creating an empty file as fallback", self.OriginalHash);
			physical = new(Path.Combine(this._tempFolder.FullName, Path.GetRandomFileName()));
			physical.Create().Dispose();
			self.OriginalHash = "";
		}

		if (this.Option.ReadOnly)
		{
			// no writing so we don't have to care about multiple files pointing to same file
			return physical;
		}

		if (this._reverseHashFileDictionary.TryGetValue(self.OriginalHash, out ReverseHashInfo? info))
		{
			using ScopedSemaphoreSlim.Scope _ = info.Lock.Enter();
			if (info.Files.Count <= 1 || !info.Files.Remove(self))
			{
				// no more files with same hash pointing to the same physical file
				return physical;
			}

			this._logger.LogDebug("Separating file hash {hash} to a new physical file, {count} same hash remains", self.OriginalHash, info.Files.Count - 1);
			FileInfo newFile = physical.CopyTo(Path.Combine(this._tempFolder.FullName, Path.GetRandomFileName()));

			return newFile;
		}

		return physical;
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
			directory.Identifier = item.ID;

			foreach (RealmFileInfo file in item.Files)
			{
				this.AddFileToDirectoryAndReverseHash(directory, file);
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
			directory.Identifier = item.ID;

			foreach (RealmFileInfo file in item.Files)
			{
				this.AddFileToDirectoryAndReverseHash(directory, file);
			}
		}
	}

	/// <inheritdoc cref="UpdateRealm(VirtualDirectory, OsuVFSBaseDirectoryType)"/>
	private void UpdateRealm(IVirtualFileSystemObject child)
	{
		VirtualPath path = child.GetFullPath();
		OsuVFSBaseDirectoryType kind = Enum.Parse<OsuVFSBaseDirectoryType>(path.DirectorySegments[0], true);
		this.UpdateRealm(this.RootDirectory.FindDirectory(path.GetDirectoryRange(0..2)).ThrowIfNull(), kind);
	}
	/// <summary>
	/// update realm for the directory of the skin of song folder (/Skins/xxxxx/) or (/Songs/xxxxx/).
	/// you are supposed to enter the root directory lock yourself
	/// </summary>
	/// <param name="directory"></param>
	private void UpdateRealm(VirtualDirectory directory, OsuVFSBaseDirectoryType kind)
	{
		int truncateCount = directory.GetFullPath().DirectorySegments.Length;
		List<RealmFlattenedFileInfo> flattenedFiles = [];
		FlattenDirectory(directory);

		if (kind == OsuVFSBaseDirectoryType.Songs)
		{
			this.RealmAccess.Write(x =>
			{
				this._logger.LogDebug("Updating beatmap set {guid}", directory.Identifier);
				UpdateCore(x.Find<BeatmapSetInfo>(directory.Identifier).ThrowIfNull(), x);
			});
		}
		else if (kind == OsuVFSBaseDirectoryType.Skins)
		{
			this.RealmAccess.Write(x =>
			{
				this._logger.LogDebug("Updating skin {guid}", directory.Identifier);
				UpdateCore(x.Find<SkinInfo>(directory.Identifier).ThrowIfNull(), x);
			});
		}
		else
		{
			throw new ArgumentException("Invalid kind", nameof(kind));
		}

		void UpdateCore(IHasRealmFiles originalEntry, Realm realm)
		{
			originalEntry.Files.Clear();
			foreach (RealmFlattenedFileInfo file in flattenedFiles)
			{
				RealmFile rawFile = realm.Find<RealmFile>(file.File.OriginalHash)
					?? realm.Add(new RealmFile() { Hash = file.File.OriginalHash });
				originalEntry.Files.Add(new(rawFile, file.Path.ToString("", null)));
			}
		}

		void FlattenDirectory(VirtualDirectory dir)
		{
			foreach (VirtualFile file in dir.Files)
			{
				flattenedFiles.Add(new(file.GetFullPath().Mutate(x => x[truncateCount..]), file));
			}
			foreach (VirtualDirectory subdir in dir.Subdirectories)
			{
				FlattenDirectory(subdir);
			}
		}
	}
	#endregion

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

	private static void SetNormalizedPath(VirtualPath path, out string NormalizedName)
	{
		NormalizedName = path.ToString("SB", null);
	}

	#region Miscellaneous
	public override int ExceptionHandler(Exception ex)
	{
		this._logger.LogError(ex, "Unhandled exception.");
		return base.ExceptionHandler(ex);
	}
	#endregion

	#region Initialization, Finalization
	public override int Init(object Host)
	{
		this._logger.LogInformation("OsuVFS initialized, host: {Host}", Host);

		return STATUS_SUCCESS; // why cant they make it an enum or something?
	}
	public override int Mounted(object Host)
	{
		using ScopedSemaphoreSlim.Scope _ = this.RootDirectoryLock.Enter();

		VirtualDirectory beatMapDir = this.RootDirectory.AddDirectory(VirtualPath.FromDirectory(nameof(OsuVFSBaseDirectoryType.Songs)));
		VirtualDirectory skinDir = this.RootDirectory.AddDirectory(VirtualPath.FromDirectory(nameof(OsuVFSBaseDirectoryType.Skins)));

		Task.WaitAll(
			Task.Run(() => this.GetBeatMapDirectories(beatMapDir)),
			Task.Run(() => this.GetSkinDirectories(skinDir)));

		this._logger.LogInformation("OsuVFS mounted, host: {Host}", Host);
		return STATUS_SUCCESS;
	}
	public override void Unmounted(object Host)
	{
		using ScopedSemaphoreSlim.Scope _ = this.RootDirectoryLock.Enter();
		this.RootDirectory.RemoveAll();
		this._logger.LogInformation("OsuVFS unmounted, host: {Host}", Host);
	}
	public override int Open(string FileName, uint CreateOptions, uint GrantedAccess, out object FileNode, out object FileDesc, out FSPFileInfo FileInfo, out string NormalizedName)
	{
		FileNode = null!;

		bool deleteOnClose = CreateOptions.HasFlag(FILE_DELETE_ON_CLOSE);
		if (deleteOnClose && this.Option.ReadOnly)
		{
			FileDesc = null!;
			FileInfo = default;
			NormalizedName = null!;
			return STATUS_MEDIA_WRITE_PROTECTED;
		}

		if (deleteOnClose && !this.IsValidWriteOperation(FileName))
		{
			FileDesc = null!;
			FileInfo = default;
			NormalizedName = null!;
			return STATUS_INVALID_DEVICE_REQUEST;
		}

		using ScopedSemaphoreSlim.Scope _ = this.RootDirectoryLock.Enter();

		VirtualDirectory? dir = this.RootDirectory.FindDirectory(VirtualPath.FromDirectory(FileName), StringComparison.OrdinalIgnoreCase);
		VirtualFile? file = this.RootDirectory.FindFile(VirtualPath.FromFile(FileName), StringComparison.OrdinalIgnoreCase);

		// the CreateOptions does not always specify whether it's opening a file or directory, so we need to check both
		if (dir is not null)
		{
			if (deleteOnClose && !dir.IsEmpty)
			{
				FileDesc = null!;
				FileInfo = default;
				NormalizedName = null!;
				return STATUS_DIRECTORY_NOT_EMPTY;
			}

			DirectoryDescriptor directoryDescriptor = new(dir)
			{
				DeleteOnClose = deleteOnClose
			};
			FileDesc = directoryDescriptor;
			FileInfo = this.CreateInfo(dir);
			SetNormalizedPath(dir.GetFullPath(), out NormalizedName);

			this._logger.LogTrace("Open: Directory {name}", FileName);

			return STATUS_SUCCESS;
		}

		if (file is null)
		{
			FileDesc = null!;
			FileInfo = default;
			NormalizedName = null!;
			return STATUS_OBJECT_NAME_NOT_FOUND;
		}

		this._logger.LogTrace("Open: File {name}", FileName);

		FileDescriptor descriptor = new(file, this.OpenStreamDefault(file.PhysicalFile))
		{
			DeleteOnClose = deleteOnClose
		};
		FileDesc = descriptor;
		FileInfo = this.CreateInfo(file.PhysicalFile);
		SetNormalizedPath(file.GetFullPath(), out NormalizedName);

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
		node.File.PhysicalFile.Refresh();

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

		using ScopedSemaphoreSlim.Scope _ = this.RootDirectoryLock.Enter();

		if (this.Option.ReadOnly)
			goto Dispose;

		if (descriptor.DeleteOnClose)
		{
			IVirtualFileSystemObject vObj = descriptor.VirtualObject;
			VirtualPath path = vObj.GetFullPath();

			if (vObj is VirtualFile file)
			{
				// let osu cleanup the file
				// file.PhysicalFile.Delete();
				this.RootDirectory.RemoveFile(path);
			}
			else if (vObj is VirtualDirectory)
			{
				this.RootDirectory.RemoveDirectory(path);
			}
			this.UpdateRealm(this.RootDirectory.FindDirectory(path.GetDirectoryRange(..2)).ThrowIfNull());

			this._logger.LogDebug("{type} removed", vObj.GetType().Name);

			goto Dispose;
		}

		if (descriptor is FileDescriptor fileDesc && fileDesc.HasEverWritten)
		{
			fileDesc.Stream.Seek(0, SeekOrigin.Begin);
			byte[] hash = SHA256.HashData(fileDesc.Stream);
			string hashString = Convert.ToHexString(hash).ToLower();

			if (fileDesc.File.OriginalHash == hashString) goto Dispose;

			DirectoryInfo newHashDir = this._cachedHashDirectories[hashString[..2]];
			FileInfo newFile = new(Path.Combine(newHashDir.FullName, hashString));

			// no need to replace the file
			if (newFile.Exists) goto Update;

			fileDesc.Stream.Dispose();
			fileDesc.File.PhysicalFile.CopyTo(newFile.FullName);
			fileDesc.File.PhysicalFile = newFile;
			fileDesc.File.OriginalHash = hashString;

		Update:
			this.UpdateRealm(fileDesc.File);
		}
		else if (descriptor is DirectoryDescriptor dirDesc && dirDesc.Directory.HasBeenRenamed)
		{
			this.UpdateRealm(dirDesc.Directory);
		}

	Dispose:
		descriptor.Dispose();
	}
	public override void Cleanup(object FileNode, object FileDesc, string FileName, uint Flags)
	{
		this._logger.LogTrace("Cleanup: {FileDesc}", FileDesc);
		if (Flags.HasFlag(CleanupDelete) && FileDesc is IDescriptor node)
		{
			node.DeleteOnClose = true;
		}
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

	public override int GetSecurityByName(string FileName, out uint FileAttributes, ref byte[] SecurityDescriptor)
	{
		this._logger.LogTrace("GetSecurityByName: {name}", FileName);

		using ScopedSemaphoreSlim.Scope _ = this.RootDirectoryLock.Enter();

		VirtualDirectory? dir = this.RootDirectory.FindDirectory(VirtualPath.FromDirectory(FileName), StringComparison.OrdinalIgnoreCase);
		VirtualFile? file = this.RootDirectory.FindFile(VirtualPath.FromFile(FileName), StringComparison.OrdinalIgnoreCase);

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

	public override int GetVolumeInfo(out VolumeInfo VolumeInfo)
	{
		VolumeInfo = new()
		{
			FreeSize = 0,
			TotalSize = 0,
		};
		VolumeInfo.SetVolumeLabel(this.Option.VolumeLabel);
		return STATUS_SUCCESS;
	}

	#endregion

	#region Write
	public override unsafe int Write(object FileNode, object FileDesc, nint Buffer, ulong Offset, uint Length, bool WriteToEndOfFile, bool ConstrainedIo, out uint BytesTransferred, out FSPFileInfo FileInfo)
	{
		this._logger.LogDebug("Write: {FileDesc}", FileDesc);

		if (this.Option.ReadOnly)
		{
			BytesTransferred = 0;
			FileInfo = default;
			return STATUS_MEDIA_WRITE_PROTECTED;
		}

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
		node.HasEverWritten = true;

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

		if (this.Option.ReadOnly)
		{
			FileInfo = default;
			return STATUS_MEDIA_WRITE_PROTECTED;
		}

		if (FileDesc is not FileDescriptor node)
		{
			FileInfo = default;
			return STATUS_INVALID_PARAMETER;
		}

		using ScopedSemaphoreSlim.Scope _ = node.Lock.Enter();
		node.Stream.Dispose();
		node.Stream = this.OpenStreamDefault(node.File.PhysicalFile, FileMode.Create);
		node.HasEverWritten = true;

		FileInfo = this.CreateInfo(node.File.PhysicalFile) with
		{
			FileSize = 0,
			AllocationSize = AllocationSize, // not sure if this will work
		};
		return STATUS_SUCCESS;
	}

	public override int SetFileSize(object FileNode, object FileDesc, ulong NewSize, bool SetAllocationSize, out FSPFileInfo FileInfo)
	{
		this._logger.LogDebug("SetFileSize: {FileDesc}, {size}", FileDesc, NewSize);

		if (this.Option.ReadOnly)
		{
			FileInfo = default;
			return STATUS_MEDIA_WRITE_PROTECTED;
		}

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
		this._logger.LogInformation("Create: {name}, {attr}", FileName, (FileAttributes_t)FileAttributes);

		if (this.Option.ReadOnly)
		{
			FileNode = null!;
			FileDesc = null!;
			FileInfo = default;
			NormalizedName = null!;
			return STATUS_MEDIA_WRITE_PROTECTED;
		}

		if (!this.IsValidWriteOperation(FileName))
		{
			FileNode = null!;
			FileDesc = null!;
			FileInfo = default;
			NormalizedName = null!;
			return STATUS_NOT_SUPPORTED;
		}

		using ScopedSemaphoreSlim.Scope _ = this.RootDirectoryLock.Enter();

		// note ignoring file attributes, granted access, create options, security descriptor etc for now, will implement later if needed
		if (FileAttributes.HasFlag((uint)FileAttributes_t.Directory))
		{
			VirtualPath fullPath = VirtualPath.FromDirectory(FileName);
			// doesnt need to check directory segment length, is valid write operation already done that
			VirtualPath parentPath = fullPath.GetDirectoryRange(..^1);

			VirtualDirectory? parent = this.RootDirectory.FindDirectory(parentPath, StringComparison.OrdinalIgnoreCase);

			if (parent is null)
			{
				FileNode = null!;
				FileDesc = null!;
				FileInfo = default;
				NormalizedName = null!;
				return STATUS_OBJECT_PATH_NOT_FOUND;
			}
			VirtualPath subDirPath = fullPath.GetDirectoryRange(^1..^0);
			if (parent.FindDirectory(subDirPath, StringComparison.OrdinalIgnoreCase) is not null)
			{
				FileNode = null!;
				FileDesc = null!;
				FileInfo = default;
				NormalizedName = null!;
				return STATUS_OBJECT_NAME_COLLISION;
			}

			VirtualDirectory dir = parent.AddDirectory(subDirPath);

			FileNode = null!;
			FileDesc = new DirectoryDescriptor(dir);
			FileInfo = this.CreateInfo(dir);
			SetNormalizedPath(dir.GetFullPath(), out NormalizedName);
			return STATUS_SUCCESS;
		}
		else
		{
			VirtualPath path = VirtualPath.FromFile(FileName);
			VirtualDirectory? parent = this.RootDirectory.FindDirectory(path);

			if (parent is null)
			{
				FileNode = null!;
				FileDesc = null!;
				FileInfo = default;
				NormalizedName = null!;
				return STATUS_OBJECT_PATH_NOT_FOUND;
			}

			VirtualPath filePath = path.GetDirectoryRange(0..0, true);
			if (parent.FindFile(filePath) is not null)
			{
				FileNode = null!;
				FileDesc = null!;
				FileInfo = default;
				NormalizedName = null!;
				return STATUS_OBJECT_NAME_COLLISION;
			}

			FileInfo physicalFile = new(Path.GetTempFileName());
			VirtualFile file = new(filePath.FileName, "", physicalFile);
			parent.AddFile(file, filePath);

			FileNode = null!;
			FileDesc = new FileDescriptor(file, this.OpenStreamDefault(physicalFile))
			{
				HasEverWritten = true // to make Close update realm
			};
			FileInfo = this.CreateInfo(physicalFile);
			SetNormalizedPath(file.GetFullPath(), out NormalizedName);
			return STATUS_SUCCESS;
		}
	}
	public override int Rename(object FileNode, object FileDesc, string FileName, string NewFileName, bool ReplaceIfExists)
	{
		this._logger.LogInformation("Rename: [overwrite: {ow}] {old} to {new}", ReplaceIfExists, FileName, NewFileName);

		if (this.Option.ReadOnly)
		{
			return STATUS_MEDIA_WRITE_PROTECTED;
		}

		if (!this.IsValidWriteOperation(NewFileName) || !this.IsValidWriteOperation(FileName))
		{
			return STATUS_NOT_SUPPORTED;
		}

		using ScopedSemaphoreSlim.Scope _2 = this.RootDirectoryLock.Enter();

		if (FileDesc is DirectoryDescriptor dirNode)
		{
			using ScopedSemaphoreSlim.Scope _ = dirNode.Lock.Enter();

			VirtualPath oldDirPath = VirtualPath.FromDirectory(FileName);
			VirtualPath newDirPath = VirtualPath.FromDirectory(NewFileName);

			VirtualDirectory? newTarget = this.RootDirectory.FindDirectory(newDirPath, StringComparison.OrdinalIgnoreCase);
			VirtualDirectory? newParent = this.RootDirectory.FindDirectory(newDirPath.GetDirectoryRange(..^1), StringComparison.OrdinalIgnoreCase);
			VirtualDirectory? oldTarget = this.RootDirectory.FindDirectory(oldDirPath, StringComparison.OrdinalIgnoreCase);
			VirtualDirectory? oldParent = oldTarget?.Parent;

			if (newTarget is not null)
			{
				return STATUS_OBJECT_NAME_COLLISION;
			}
			if (newParent is null)
			{
				return STATUS_OBJECT_PATH_NOT_FOUND;
			}

			if (oldParent is null || oldTarget is null)
			{
				this._logger.LogWarning("Failed to find parent or itself for {FileName} during rename operation", FileName);
				return STATUS_UNEXPECTED_IO_ERROR;
			}

			if (!oldTarget.GetFullPath().DirectorySegments[0..2].SequenceEqual(newParent.GetFullPath().DirectorySegments[0..2]))
			{
				// only allows renaming within the same song-specific or skin-specific folder
				return STATUS_NOT_SUPPORTED;
			}

			VirtualDirectory oldDir = dirNode.Directory;
			this.RootDirectory.RemoveDirectory(oldDir.GetFullPath());
			newParent.AddDirectory(oldDir);
			oldDir.Name = newDirPath.DirectorySegments[^1];
			oldDir.HasBeenRenamed = true;

			return STATUS_SUCCESS;
		}
		else if (FileDesc is FileDescriptor node)
		{
			using ScopedSemaphoreSlim.Scope _ = node.Lock.Enter();

			VirtualPath oldPath = node.File.GetFullPath();
			VirtualPath newPath = VirtualPath.FromFile(NewFileName);

			VirtualFile? newTarget = this.RootDirectory.FindFile(newPath, StringComparison.OrdinalIgnoreCase);
			VirtualDirectory? newParent = this.RootDirectory.FindDirectory(newPath, StringComparison.OrdinalIgnoreCase);
			VirtualFile oldTarget = node.File;
			VirtualDirectory? oldParent = node.File.Parent;

			if (oldParent is null)
			{
				this._logger.LogWarning("Failed to find parent directory for {FileName} during rename operation", FileName);
				return STATUS_UNEXPECTED_IO_ERROR;
			}
			if (this.RootDirectory.FindFile(VirtualPath.FromFile(FileName), StringComparison.OrdinalIgnoreCase).ThrowIfNull().GetFullPath()
				!= oldTarget.GetFullPath())
			{
				this._logger.LogWarning("File name {f} does match file desc path {d}", FileName, oldTarget.GetFullPath());
				return STATUS_UNEXPECTED_IO_ERROR;
			}

			if (newParent is null)
			{
				return STATUS_OBJECT_PATH_NOT_FOUND;
			}

			if (newTarget is not null)
			{
				if (!ReplaceIfExists)
				{
					return STATUS_OBJECT_NAME_COLLISION;
				}
				this.RootDirectory.RemoveFile(newTarget.GetFullPath());
			}

			this.RootDirectory.RemoveFile(oldTarget.GetFullPath());

			node.File = new(newPath.FileName, node.File.OriginalHash, node.File.PhysicalFile);
			this.RootDirectory.AddFile(node.File, newParent.GetFullPath());
			node.HasEverWritten = true;

			return STATUS_SUCCESS;
		}

		return STATUS_INVALID_PARAMETER;
	}
	public override int CanDelete(object FileNode, object FileDesc, string FileName)
	{
		if (this.Option.ReadOnly)
		{
			return STATUS_MEDIA_WRITE_PROTECTED;
		}

		if (!this.IsValidWriteOperation(FileName))
		{
			return STATUS_NOT_SUPPORTED;
		}

		// open handle checking is done by fsp
		if (FileDesc is DirectoryDescriptor dirNode)
		{
			if (!dirNode.Directory.IsEmpty)
			{
				return STATUS_DIRECTORY_NOT_EMPTY;
			}
			return STATUS_SUCCESS;
		}
		else if (FileDesc is FileDescriptor fileNode)
		{
			return STATUS_SUCCESS;
		}

		return STATUS_INVALID_PARAMETER;
	}
	#endregion
}
