using System.Diagnostics.CodeAnalysis;

namespace OsuLazerFSMounter;
public static class Helper
{
	public static bool HasFlag(this uint value, uint flag)
	{
		return (value & flag) == flag;
	}
	public static bool HasFlag(this int value, int flag)
	{
		return (value & flag) == flag;
	}

	[return: NotNull()]
	public static T ThrowIfNull<T>(this T obj)
	{
		if (obj is null)
		{
			throw new ArgumentNullException(nameof(obj), "Object cannot be null.");
		}
		return obj;
	}

	public static bool IsDirectory(string path)
	{
		path = path.TrimEnd();
		return path.EndsWith("/") || path.EndsWith("\\");
	}
}
