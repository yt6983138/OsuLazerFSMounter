using Fsp;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using osu.Game.Database;
using System.CommandLine;

namespace OsuLazerFSMounter;
public class OsuVFSConsoleService : Service
{
	private static readonly Option<string> RealmFileOption = new("--realm-file")
	{
		Description = "Path to the realm file. Put 'DEBUG' to use osu-development file automatically, and 'RELEASE' to use osu release automatically. Defaults to " +
#if DEBUG
			"'DEBUG'"
#else
			"'RELEASE'"
#endif
	};
	private static readonly Option<bool> RunLiveOption = new("--run-live")
	{
		Description = "Whether to run with osu open or not. Write access is automatically disabled if present. See documentation for more details."
	};
	private static readonly Option<bool> CanWriteOption = new("--can-write")
	{
		Description = "Whether to allow write operations. This option is ignored if --run-live is set."
	};
	private static readonly Option<char?> MountPointOption = new("--mount-point")
	{
		Description = "The drive letter to mount the virtual file system to. Will pick a free one if not specified."
	};

	private FileSystemHost? _host;
	private OsuVFS? _osuVFS;
	private ILogger<OsuVFSConsoleService>? _logger;

	public OsuVFSConsoleService()
		: base(nameof(OsuVFSConsoleService)) { }

	protected override int ExceptionHandler(Exception ex)
	{
		this._logger?.LogError(ex, "An unhandled exception occurred.");
		return base.ExceptionHandler(ex);
	}
	protected override void OnStart(string[] Args)
	{
		ILoggerFactory loggerFactory = LoggerFactory.Create(x => x.AddNLog());
		this._logger = loggerFactory.CreateLogger<OsuVFSConsoleService>();

#pragma warning disable IDE0028 // Simplify collection initialization
		RootCommand rootCommand = new("OsuVFS CLI"); // Visual studio tries to simplify this by using collection initialization, which causes compile errors
#pragma warning restore IDE0028 // Simplify collection initialization
		rootCommand.Options.Add(RealmFileOption);
		rootCommand.Options.Add(RunLiveOption);
		rootCommand.Options.Add(CanWriteOption);
		rootCommand.Options.Add(MountPointOption);

		ParseResult parseResult = rootCommand.Parse(Args);

		// not implemented yet, plan is copying the realm file to a temp location and use that
		bool runLive = parseResult.GetValue(RunLiveOption);
		bool canWrite = parseResult.GetValue(CanWriteOption);
		char? mountPointRaw = parseResult.GetValue(MountPointOption);
		string realmFile = parseResult.GetValue(RealmFileOption) ??
#if DEBUG
			"DEBUG";
#else
			"RELEASE";
#endif

		canWrite = !runLive && canWrite;

		string mountPoint;
		if (!mountPointRaw.HasValue)
		{
			List<char> availableLetters = Helper.GetAvailableDriveLetters();
			availableLetters.Remove('A');
			availableLetters.Remove('B');
			mountPoint = availableLetters[0] + ":";
		}
		else
		{
			mountPoint = mountPointRaw.Value + ":";
		}

		DirectoryInfo filesDirectory;
		DirectoryInfo realmDirectory;
		string realmFileName;
		if (realmFile == "DEBUG")
		{
			DirectoryInfo debugDir = new(Environment.ExpandEnvironmentVariables(@"%appdata%\osu-development"));
			// since osu-dev uses realm files with names like client_*.realm, picks the highest version as that is likely to be the file which osu is using
			FileInfo[] files = debugDir.GetFiles("client*.realm");
			(FileInfo File, int Version)[] versions = files.Select(f =>
			{
				string nameWithoutExtension = Path.GetFileNameWithoutExtension(f.Name);
				string? versionPart = nameWithoutExtension.Split('_').LastOrDefault();
				if (versionPart is not null && int.TryParse(versionPart, out int version))
				{
					return (f, version);
				}
				return (f, -1);
			}).ToArray();
			Array.Sort(versions, (a, b) => b.Version.CompareTo(a.Version)); // pick newest one
			realmFileName = versions[0].File.Name;
			realmDirectory = new(versions[0].File.DirectoryName.ThrowIfNull());
			filesDirectory = new(Environment.ExpandEnvironmentVariables(@"%appdata%\osu-development\files"));
		}
		else if (realmFile == "RELEASE")
		{
			realmFileName = "client.realm";
			realmDirectory = new(Environment.ExpandEnvironmentVariables(@"%appdata%\osu"));
			filesDirectory = new(Environment.ExpandEnvironmentVariables(@"%appdata%\osu\files"));
		}
		else
		{
			realmFileName = Path.GetFileName(realmFile);
			realmDirectory = new(Path.GetDirectoryName(realmFile).ThrowIfNull());
			filesDirectory = new(Path.Combine(realmDirectory.FullName, "files"));
		}

		OsuFileStorage storage = new(realmDirectory.FullName);
		RealmAccess realmAccess = new(storage, realmFileName);

		this._osuVFS = new(realmAccess, filesDirectory, loggerFactory.CreateLogger<OsuVFS>(), new()
		{
			ReadOnly = !canWrite,
			VolumeLabel = realmFileName == "DEBUG" ? "osu-development" : "osu"
		});
		this._host = new(this._osuVFS)
		{
			FileSystemName = nameof(OsuVFS),
			CaseSensitiveSearch = true
		};

		// SetVolumeLabel, Flush(Volume), Create, Cleanup(Delete), SetInformation(Rename) shares the same lock, so they are all atomic with each other
		// Open, ReadDirectory, SetDisposition, GetVolumeInfo cannot run at the same time as the above operations, but they can run at the same time as each other
		this._host.Mount(mountPoint, Synchronized: true);

		this._logger?.LogInformation("Mounting started.");
	}
	protected override void OnStop()
	{
		this._host?.Dispose();
		this._logger?.LogInformation("Mounting stopped.");
	}
}
