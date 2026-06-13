using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace OsuLazerFSMounter.FileSystem;
public struct VirtualPath : IFormattable, IEquatable<VirtualPath>, IEqualityOperators<VirtualPath, VirtualPath, bool>
{
	public string[] FullSegments { get; set; }

	public readonly bool HasFileName => this.FullSegments.Length > 0 && !string.IsNullOrEmpty(this.FullSegments[^1]);
	public readonly string FileName => this.HasFileName ? this.FullSegments[^1] : "";
	/// <summary>
	/// immutable
	/// </summary>
	public readonly Span<string> DirectorySegments => this.FullSegments.Length > 1 ? this.FullSegments[..^1] : [];

	public VirtualPath(string path)
	{
		this.FullSegments = BreakIntoDirectoryPathsAndSanitize(path);
	}
	public VirtualPath(string[] fullSegments)
	{
		this.FullSegments = fullSegments;
	}

	public static VirtualPath FromDirectory(string path)
	{
		string[] pathSegments = BreakIntoDirectoryPathsAndSanitize(path, false);
		if (pathSegments.Length == 0 || !string.IsNullOrEmpty(pathSegments[^1]))
		{
			Array.Resize(ref pathSegments, pathSegments.Length + 1);
			pathSegments[^1] = "";
		}
		return new(pathSegments);
	}
	public static VirtualPath FromFile(string path)
	{
		string[] pathSegments = BreakIntoDirectoryPathsAndSanitize(path, false);
		return new(pathSegments);
	}

	public static bool operator !=(VirtualPath left, VirtualPath right)
	{
		return !(left == right);
	}
	public static bool operator ==(VirtualPath left, VirtualPath right)
	{
		return left.FullSegments.SequenceEqual(right.FullSegments);
	}

	public readonly bool Equals(VirtualPath other)
	{
		return this == other;
	}

	public override readonly bool Equals([NotNullWhen(true)] object? obj)
	{
		if (obj is not VirtualPath other)
			return false;

		return this == other;
	}
	public override readonly int GetHashCode()
	{
		return HashCode.Combine(this.FullSegments);
	}
	/// <summary>
	/// can contain few flags: (E)nd with slash for directories, (S)tart with slash, (B)ackslash as separator.
	/// 
	/// multiple flags can be combined, ex. "ES".
	/// 
	/// default is "ES"
	/// </summary>
	/// <param name="format"></param>
	/// <param name="formatProvider"></param>
	/// <returns></returns>
	public readonly string ToString(string? format, IFormatProvider? formatProvider)
	{
		format = format?.Trim() ?? "";

		bool endWithSlash = format.Contains('E');
		bool startWithSlash = format.Contains('S');
		bool useBackslash = format.Contains('B');

		char separator = useBackslash ? '\\' : '/';

		string result = string.Join(separator, this.FullSegments);
		if (!endWithSlash)
			result = result.TrimEnd(separator);
		if (startWithSlash)
			result = separator + result;

		return result;
	}
	public override readonly string ToString()
	{
		return this.ToString("ES", null);
	}

	public readonly VirtualPath AppendDirectory(params IEnumerable<string> segments)
	{
		return new(this.DirectorySegments.ToArray().Concat(segments).Append(this.FileName).ToArray());
	}
	public readonly VirtualPath Append(params IEnumerable<string> segments)
	{
		if (this.HasFileName)
			return new(this.FullSegments.Concat(segments).ToArray());
		else return new(this.FullSegments.Take(this.FullSegments.Length - 1).Concat(segments).ToArray());
	}

	public readonly VirtualPath GetDirectoryRange(Range range, bool leaveFileName = false)
	{
		VirtualPath result = new([.. this.DirectorySegments[range], ""]);
		if (leaveFileName)
		{
			result = result.WithFileName(this.FileName);
		}
		return result;
	}
	public readonly VirtualPath Mutate(Func<string[], string[]> mutator)
	{
		return new(mutator.Invoke(this.FullSegments));
	}
	public readonly VirtualPath WithFileName(string fileName)
	{
		if (this.FullSegments.Length == 0)
			return new([fileName]);

		string[] newSegments = this.FullSegments.ToArray();
		newSegments[^1] = fileName;
		return new(newSegments);
	}

	/// <summary>
	/// last one is always file name, string.Empty if path ends with a separator. ignores empty segments and trims whitespace from segments.
	/// this also trims starting slashes, so the first segment is always a directory name or file name
	/// </summary>
	/// <param name="path"></param>
	/// <returns></returns>
	public static string[] BreakIntoDirectoryPathsAndSanitize(string path, bool doNotEndWithEmptyName = true)
	{
		string[] paths = path.Split(['\\', '/']);

		if (paths.Length == 0)
			return [];

		string[] result = paths
			.SkipLast(1)
			.Where(x => !string.IsNullOrWhiteSpace(x))
			.Append(paths[^1])
			.Select(x => x.Trim())
			.ToArray();

		if (doNotEndWithEmptyName && result[^1] == "")
			result = result[..^1];

		return result;
	}
}
