using Fsp;
using Microsoft.Extensions.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Screens.Select;
using osu.Game.Skinning;
using OsuLazerFSMounter;
using OsuLazerFSMounter.FileSystem;
using OsuLazerFSMounter.Utility;
using Realms;

namespace osu.Game.Rulesets.OsuVFSPlugin;
public class OsuVFSRulesetService : Service
{
	private record class MountedContext(OsuVFS VFS, FileSystemHost Host);

	private static OsuVFSRulesetService? _instance;

	public static OsuVFSRulesetService GetOrCreateInstance(
		RealmAccess realmAccess,
		DirectoryInfo fileDirector,
		BeatmapManager beatmapManager,
		SkinManager skinManager,
		OsuGame game,
		GameHost host)
	{
		if (_instance is null)
		{
			_instance = new(realmAccess, fileDirector, beatmapManager, skinManager, game, host);
			Task.Run(() =>
			{
				int code = _instance.Run();
				if (code != 0)
					_instance._logger.LogError("Service exited with code {code:X}.", code);
			});
		}
		return _instance;
	}

	public static OsuVFSRulesetService GetInstance()
		=> _instance ?? throw new InvalidOperationException("Service has not been created yet. Call GetOrCreateInstance first.");

	private readonly RealmAccess _realmAccess;
	private readonly DirectoryInfo _fileDirectory;
	private readonly ILogger<OsuVFSRulesetService> _logger;
	private readonly BeatmapManager _beatmapManager;
	private readonly SkinManager _skinManager;
	private readonly OsuGame _game;
	private readonly GameHost _host;

	private readonly IDisposable _beatmapSubscription;
	private readonly IDisposable _skinSubscription;

	private readonly ResourceAccessor<KeyedCollectionProxy<Guid, PartialBeatmapSetInfo>> _beatmapSetInfoCache = new(1, 1, new(x => x.ID));
	private readonly ResourceAccessor<KeyedCollectionProxy<Guid, PartialSkinInfo>> _skinInfoCache = new(1, 1, new(x => x.ID));

	private readonly ResourceAccessor<MountedContext?> _mountedContext = new(1, 1, null);

	public ILoggerFactory LoggerFactory { get; private init; }
	public OsuVFSStartOption Options
	{
		get => field;
		set
		{
			bool isMounted = false;
			this._mountedContext.Access((ref MountedContext? x) => isMounted = x is not null);

			if (isMounted)
				throw new InvalidOperationException("Cannot set options after the filesystem has mounted.");

			field = value;
		}
	} = new("", true);

	public OsuVFSRulesetService(
		RealmAccess realmAccess,
		DirectoryInfo fileDirectory,
		BeatmapManager beatmapManager,
		SkinManager skinManager,
		OsuGame game,
		GameHost host) : base(nameof(OsuVFSRuleset))
	{
		this.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(x => x
			.AddProvider(new OsuLoggerProvider())
			.SetMinimumLevel(
#if DEBUG
				LogLevel.Debug
#else
				LogLevel.Information
#endif
			));

		this._realmAccess = realmAccess;
		this._fileDirectory = fileDirectory;
		this._logger = this.LoggerFactory.CreateLogger<OsuVFSRulesetService>();
		this._beatmapManager = beatmapManager;
		this._skinManager = skinManager;
		this._game = game;
		this._host = host;

		this._beatmapSubscription = this._realmAccess.RegisterForNotifications(x => x.All<BeatmapSetInfo>(), this.RealmAccess_BeatmapSetInfoChanged);
		this._skinSubscription = this._realmAccess.RegisterForNotifications(x => x.All<SkinInfo>(), this.RealmAccess_SkinInfoChanged);
	}

	// this method is kinda going insane
	private void UpdateVFSFromExternalChange<TModel, TLocalModel>(
		IRealmCollection<TModel> sender,
		ChangeSet? changes,
		ResourceAccessor<KeyedCollectionProxy<Guid, TLocalModel>>.AccessorScope accessor,
		OsuVFSBaseDirectoryType baseType,
		Func<TModel, TLocalModel> localConverter,
		Func<OsuVFS, TModel, VirtualDirectory> addEntireConverter)

		where TLocalModel : IOfflineRealmInfo
		where TModel : IHasRealmFiles, IHasGuidPrimaryKey
	{
		string typeName = typeof(TModel).Name;

		if (changes is null)
		{
			this._logger.LogDebug("Initial notification received for {type} collection.", typeName);
			foreach (TModel item in sender)
			{
				accessor.Value.Add(localConverter.Invoke(item));
			}
			return;
		}

		TModel[] addedItems =
			changes.InsertedIndices.Select(index => sender.ElementAt(index)).ToArray();
		(TModel, TLocalModel, int)[] modifiedItems =
			changes.ModifiedIndices.Select(index => (sender.ElementAt(index), accessor.Value[index], index)).ToArray();
		TLocalModel[] removedItems =
			changes.DeletedIndices.Select(index => accessor.Value[index]).ToArray();

		foreach (TModel item in addedItems)
		{
			accessor.Value.Add(localConverter.Invoke(item));
		}
		foreach ((TModel? newItem, TLocalModel? oldItem, int index) in modifiedItems)
		{
			accessor.Value[index] = localConverter.Invoke(newItem);
		}
		foreach (TLocalModel item in removedItems)
		{
			accessor.Value.Remove(item);
		}

		using ResourceAccessor<MountedContext?>.AccessorScope vfsAccessor = this._mountedContext.EnterAccessorScope();
		if (vfsAccessor.Value is null)
			return; // not mounted yet

		using ResourceAccessor<VirtualDirectory>.AccessorScope rootDirectory = vfsAccessor.Value.VFS.RootDirectory.EnterAccessorScope();

		VirtualDirectory beatmapDirectory = rootDirectory.Value.FindDirectory(VirtualPath.FromDirectory(baseType.ToString()))
			.ThrowIfNull();

		foreach (TModel item in addedItems)
		{
			this._logger.LogDebug("External {type} addition: {guid}", typeName, item.ID);

			beatmapDirectory.AddDirectory(
				addEntireConverter.Invoke(vfsAccessor.Value.VFS, item));
		}

		using ResourceAccessor<List<IDescriptor>>.AccessorScope descriptorAccessor = vfsAccessor.Value.VFS.OpenDescriptors.EnterAccessorScope();
		foreach (TLocalModel item in removedItems)
		{
			VirtualDirectory? dir = beatmapDirectory.Subdirectories.FirstOrDefault(x => x.Identifier == item.ID);
			if (dir is null)
			{
				this._logger.LogWarning("Could not find directory for removed {type} with ID {id}.", typeName, item.ID);
				continue;
			}

			VirtualPath directoryPath = dir.GetFullPath();

			this._logger.LogDebug("External directory removal: {path}", directoryPath);

			beatmapDirectory.Subdirectories.Remove(dir);

			foreach (IDescriptor descriptor in descriptorAccessor.Value)
			{
				if (descriptor.VirtualObject.GetFullPath().DirectorySegments.HasPrefixOf(directoryPath.DirectorySegments))
					descriptor.Invalidate();
			}
		}
		foreach ((TModel? newItem, TLocalModel? oldItem, int index) in modifiedItems)
		{
			TLocalModel newInfo = localConverter.Invoke(newItem);

			VirtualDirectory? dir = beatmapDirectory.Subdirectories.FirstOrDefault(x => x.Identifier == newInfo.ID);
			if (dir is null)
			{
				this._logger.LogWarning("Could not find directory for modified {type} with ID {id}.", typeName, newInfo.ID);
				continue;
			}

			// we can't just compare the new and old info for *changed* files, because the new info may be added from the vfs itself,
			// so we need to compare each file instead. if any file is different, we need to invalidate the descriptor
			// however, we can still check if the new and old info are equal, because if they are, we don't need to do anything

			if (newInfo.Files.UnorderedSequenceEqual(oldItem.Files, x => x.GetHashCode(), out _, out _))
				goto CheckNameOnly;

			VirtualPath dirPath = dir.GetFullPath();
			foreach (RealmFileInfo file in newInfo.Files)
			{
				VirtualPath oldPath = VirtualPath.FromFile(file.Path);
				VirtualFile? oldFile = dir.FindFile(oldPath);
				if (oldFile is null)
				{
					// add file
					this._logger.LogDebug("External file addition detected: {dir}{file:E}", dirPath, oldPath);
					VirtualFile newFile = new(oldPath.FileName, file.Hash, vfsAccessor.Value.VFS.LazyFetchFileInfo);

					dir.AddFile(newFile, oldPath);
					vfsAccessor.Value.VFS.TryIncreaseLazyCacheCount(newFile);
				}
				else if (oldFile.Hash != file.Hash)
				{
					// file is changed, invalidate descriptors
					this._logger.LogDebug("External file modification detected: {dir}/{file}, from {oldHash} to {newHash}",
						dirPath, oldPath, oldFile.Hash, file.Hash);
					oldFile.Hash = file.Hash;
					oldFile.PhysicalFileLazy = vfsAccessor.Value.VFS.LazyFetchFileInfo;
					oldFile.InvalidateCachedPhysicalFile();
					vfsAccessor.Value.VFS.TryIncreaseLazyCacheCount(oldFile);

					VirtualPath path = oldFile.GetFullPath();
					foreach (IDescriptor? item in descriptorAccessor.Value.Where(x => x.VirtualObject.GetFullPath() == path))
						item.Invalidate();
				}
			}

			// deleted files
			List<FlattenedFile> flattenedFiles = dir.FlattenFiles(true);
			foreach (FlattenedFile item in flattenedFiles)
			{
				if (newInfo.Files.Any(x => VirtualPath.FromFile(x.Path) == item.Path))
					continue;

				this._logger.LogDebug("External file removal detected: {dir}{file:E}", dirPath, item.Path);

				// deleted file
				foreach (IDescriptor? descriptor in descriptorAccessor.Value.Where(x => x.VirtualObject == item.File))
					descriptor.Invalidate();

				item.File.Parent?.Files.Remove(item.File);
			}

		CheckNameOnly:
			string name = newInfo.GetDirectoryName();
			dir.Name = name;
		}
	}
	private void RealmAccess_BeatmapSetInfoChanged(IRealmCollection<BeatmapSetInfo> sender, ChangeSet? changes)
	{
		using ResourceAccessor<KeyedCollectionProxy<Guid, PartialBeatmapSetInfo>>.AccessorScope accessor = this._beatmapSetInfoCache.EnterAccessorScope();

		this.UpdateVFSFromExternalChange(
			sender,
			changes,
			accessor,
			OsuVFSBaseDirectoryType.Songs,
			x => new(x),
			(vfs, x) => vfs.GetBeatMapDirectory(new(x)));
	}
	private void RealmAccess_SkinInfoChanged(IRealmCollection<SkinInfo> sender, ChangeSet? changes)
	{
		using ResourceAccessor<KeyedCollectionProxy<Guid, PartialSkinInfo>>.AccessorScope accessor = this._skinInfoCache.EnterAccessorScope();

		this.UpdateVFSFromExternalChange(
			sender,
			changes,
			accessor,
			OsuVFSBaseDirectoryType.Skins,
			x => new(x),
			(vfs, x) => vfs.GetSkinDirectory(new(x)));
	}

	private void OsuVFS_RealmPostUpdate(VirtualDirectory skinOrSongDirectory, OsuVFSBaseDirectoryType type)
	{
		if (type == OsuVFSBaseDirectoryType.Songs)
		{
			using ResourceAccessor<MountedContext?>.AccessorScope accessor = this._mountedContext.EnterAccessorScope();

			this._logger.LogDebug("Invalidating beatmap set {set}", skinOrSongDirectory.Name);
			accessor.Value.ThrowIfNull().VFS.RealmAccess.Run(x =>
			{
				BeatmapSetInfo? set = x.Find<BeatmapSetInfo>(skinOrSongDirectory.Identifier);
				if (set is null)
				{
					this._logger.LogWarning("Cannot find set currently updating: {id}", skinOrSongDirectory.Identifier);
					return;
				}

				((IWorkingBeatmapCache)this._beatmapManager).Invalidate(set);

				if (this._game.ScreenStack.CurrentScreen is not SongSelect songSelect)
				{
					this._logger.LogDebug("Not in song select, skipping invalidation.");
					return;
				}

				if (songSelect.Beatmap.Value.BeatmapSetInfo.ID != set.ID)
					return;

				// TODO: reloading current beatmap this way is extremely buggy, and may randomly throw IOException, also sometimes the game says "oh the hash isn't same, no i ain't loading that"
				// maybe i shouldnt do it in such invasive way?
				this._host.UpdateThread.Scheduler.AddOnce(async () =>
				{
					WorkingBeatmap currentBeatmap = songSelect.Beatmap.Value;
					songSelect.Beatmap.SetDefault();
					await Task.Delay(200);
					songSelect.Beatmap.Value = currentBeatmap;
				});
			});
		}
	}
	private void OsuVFS_RealmPreUpdate(VirtualDirectory skinOrSongDirectory, OsuVFSBaseDirectoryType type)
	{
	}

	public void Mount()
	{
		using ResourceAccessor<MountedContext?>.AccessorScope context = this._mountedContext.EnterAccessorScope();

		if (context.Value is not null)
			throw new InvalidOperationException("Service has already been started.");

		// the args parameter is ignored, options should be set through the Options property before starting the service
		OsuVFS vfs = new(this._realmAccess, this._fileDirectory, this.LoggerFactory.CreateLogger<OsuVFS>(), new()
		{
			ReadOnly = this.Options.ReadOnly,
			VolumeLabel = "osu ruleset plugin"
		});
		FileSystemHost host = new(vfs);

		context.Value = new(vfs, host);

		vfs.RealmPreUpdate += this.OsuVFS_RealmPreUpdate;
		vfs.RealmPostUpdate += this.OsuVFS_RealmPostUpdate;

		string? mountPoint = null;
		if (!string.IsNullOrEmpty(this.Options.MountPoint))
		{
			// this is important, if the mount point is not trimmed the CreateFileW will throw `read access violation. **FilePart** was nullptr.`
			mountPoint = this.Options.MountPoint.Trim().TrimEnd('\\', '/');
		}

		int result = host.Mount(mountPoint);
		if (result != FileSystemBase.STATUS_SUCCESS)
		{
			throw new InvalidOperationException($"Failed to mount the filesystem. Error code: {result:X}");
		}
	}

	public void Unmount()
	{
		this._mountedContext.Access((ref x) =>
		{
			if (x is null)
				throw new InvalidOperationException("Service has not been started yet.");

			x.VFS.RealmPostUpdate -= this.OsuVFS_RealmPostUpdate;
			x.VFS.RealmPreUpdate -= this.OsuVFS_RealmPreUpdate;
			x.Host.Unmount();
			x.Host.Dispose();
			x = null;
		});
	}

	protected override int ExceptionHandler(Exception ex)
	{
		this._logger.LogError(ex, "An unhandled exception occurred.");
		return base.ExceptionHandler(ex);
	}

	protected override void OnStop()
	{
		this._beatmapSubscription.Dispose();
		this._skinSubscription.Dispose();
		this._beatmapSetInfoCache.Dispose();
		this._skinInfoCache.Dispose();
		base.OnStop();
	}
}
