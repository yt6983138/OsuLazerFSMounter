using Fsp;
using Microsoft.Extensions.Logging;
using osu.Game.Database;
using OsuLazerFSMounter;
using OsuLazerFSMounter.Utility;

namespace osu.Game.Rulesets.OsuVFSPlugin;
public class OsuVFSRulesetService : Service
{
	private static OsuVFSRulesetService? _instance;

	public static OsuVFSRulesetService GetOrCreateInstance(RealmAccess realmAccess, DirectoryInfo fileDirector)
	{
		if (_instance is null)
		{
			_instance = new OsuVFSRulesetService(realmAccess, fileDirector);
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

	private OsuVFS? _osuVFS;
	private FileSystemHost? _host;

	private volatile int _hasMounted = 0;

	public ILoggerFactory LoggerFactory { get; private init; }
	public bool HasStarted => this._hasMounted != 0;
	public OsuVFSStartOption Options
	{
		get => field;
		set
		{
			if (this._hasMounted != 0)
				throw new InvalidOperationException("Cannot set options after the service has started.");

			field = value;
		}
	} = new(OsuVFSMountPoint.Auto, true);

	public OsuVFSRulesetService(RealmAccess realmAccess, DirectoryInfo fileDirectory)
		: base(nameof(OsuVFSRuleset))
	{
		this._realmAccess = realmAccess;
		this._fileDirectory = fileDirectory;
		this.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(x => x.AddProvider(new OsuLoggerProvider()));
		this._logger = this.LoggerFactory.CreateLogger<OsuVFSRulesetService>();
	}

	public void Mount()
	{
		if (Interlocked.CompareExchange(ref this._hasMounted, 1, 0) != 0)
			throw new InvalidOperationException("Service has already been started.");

		// the args parameter is ignored, options should be set through the Options property before starting the service
		this._osuVFS = new OsuVFS(this._realmAccess, this._fileDirectory, this.LoggerFactory.CreateLogger<OsuVFS>(), new()
		{
			ReadOnly = this.Options.ReadOnly,
			VolumeLabel = "osu ruleset plugin"
		});
		this._host = new FileSystemHost(this._osuVFS);

		List<char> freeDriveLetters = Helper.GetAvailableDriveLetters();
		freeDriveLetters.Remove('A');
		freeDriveLetters.Remove('B');

		string mountPoint;
		if (this.Options.MountPoint == OsuVFSMountPoint.Auto)
		{
			mountPoint = freeDriveLetters[0] + ":";
		}
		else
		{
			char desiredMountPoint = this.Options.MountPoint.ToString()[0];
			if (!freeDriveLetters.Contains(desiredMountPoint))
				desiredMountPoint = freeDriveLetters[0];
			mountPoint = desiredMountPoint + ":";
		}

		this._host.Mount(mountPoint);
	}
	public void Unmount()
	{
		this._host?.Dispose();
		this._host = null;
		this._osuVFS = null;

		this._hasMounted = 0;
	}

	protected override int ExceptionHandler(Exception ex)
	{
		this._logger.LogError(ex, "An unhandled exception occurred.");
		return base.ExceptionHandler(ex);
	}
}
