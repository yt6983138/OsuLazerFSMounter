using Fsp;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using osu.Game.Database;

namespace OsuLazerFSMounter;
public class OsuVFSService : Service
{
	public OsuVFSService()
		: base(nameof(OsuVFSService)) { }

	private FileSystemHost? _host;
	private OsuVFS? _osuVFS;
	private ILogger<OsuVFSService>? _logger;

	protected override int ExceptionHandler(Exception ex)
	{
		this._logger?.LogError(ex, "An unhandled exception occurred.");
		return base.ExceptionHandler(ex);
	}
	protected override void OnStart(string[] Args)
	{
		ILoggerFactory loggerFactory = LoggerFactory.Create(x => x.AddNLog());
		this._logger = loggerFactory.CreateLogger<OsuVFSService>();

		OsuFileStorage storage = new(Environment.ExpandEnvironmentVariables(@"%appdata%\osu-development"));
		RealmAccess realmAccess = new(storage, "client_51.realm");

		this._osuVFS = new(realmAccess, new(Environment.ExpandEnvironmentVariables(@"%appdata%\osu-development\files")), loggerFactory.CreateLogger<OsuVFS>())
		{
			VolumeLabel = "osu-development"
		};
		this._host = new(this._osuVFS)
		{
			FileSystemName = nameof(OsuVFS),
			CaseSensitiveSearch = true
		};

		// SetVolumeLabel, Flush(Volume), Create, Cleanup(Delete), SetInformation(Rename) shares the same lock, so they are all atomic with each other
		// Open, ReadDirectory, SetDisposition, GetVolumeInfo cannot run at the same time as the above operations, but they can run at the same time as each other
		this._host.Mount("K:", Synchronized: false);

		this._logger?.LogInformation("Mounting started.");
	}
	protected override void OnStop()
	{
		this._host?.Dispose();
		this._logger?.LogInformation("Mounting stopped.");
	}
}
