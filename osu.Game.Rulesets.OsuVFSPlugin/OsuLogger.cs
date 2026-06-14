using Microsoft.Extensions.Logging;
using osu.Framework.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using OsuLogger = osu.Framework.Logging.Logger;
using OsuLogLevel = osu.Framework.Logging.LogLevel;

namespace osu.Game.Rulesets.OsuVFSPlugin;
public class OsuLogger<T> : ILogger<T>
{
	private readonly string _categoryName;

	public OsuLogger(string? categoryName = null)
	{
		this._categoryName = categoryName ?? typeof(T).Name;
	}

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull
	{
		return null; // not supported
	}
	public bool IsEnabled(LogLevel logLevel)
	{
		// log levels: (osu int, osu string, ms int)
		// 0 - Debug - 1
		// 1 - Verbose - 1
		// 2 - Important - 2 (Info)
		// 3 - Error - 4
		if (!OsuLogger.Enabled) return false;

		return OsuLogger.Level switch
		{
			OsuLogLevel.Debug => logLevel >= LogLevel.Debug,
			OsuLogLevel.Verbose => logLevel >= LogLevel.Debug,
			OsuLogLevel.Important => logLevel >= LogLevel.Information,
			OsuLogLevel.Error => logLevel >= LogLevel.Error,
			_ => throw new ArgumentOutOfRangeException()
		};
	}

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		string message = formatter(state, exception);
		message = $"[{this._categoryName}] ({eventId.Id}) {message}";

		OsuLogLevel osuLogLevel = logLevel switch
		{
			LogLevel.Trace => OsuLogLevel.Debug,
			LogLevel.Debug => OsuLogLevel.Debug,
			LogLevel.Information => OsuLogLevel.Important,
			LogLevel.Warning => OsuLogLevel.Important,
			LogLevel.Error => OsuLogLevel.Error,
			LogLevel.Critical => OsuLogLevel.Error,
			_ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null)
		};
		OsuLogger.Log(message, LoggingTarget.Runtime, osuLogLevel);
	}
}

public sealed class OsuLoggerProvider : ILoggerProvider
{
	public ILogger CreateLogger(string categoryName)
	{
		return new OsuLogger<OsuLogger>(categoryName);
	}
	public void Dispose()
	{
	}
}
