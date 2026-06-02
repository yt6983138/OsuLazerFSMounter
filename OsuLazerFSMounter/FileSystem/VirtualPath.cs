namespace OsuLazerFSMounter.FileSystem;
public struct VirtualPath
{
	public string[] FullSegments { get; set; }

	public readonly bool HasFileName => this.FullSegments.Length > 0 && !string.IsNullOrEmpty(this.FullSegments[^1]);
	public readonly string FileName => this.HasFileName ? this.FullSegments[^1] : "";
	/// <summary>
	/// immutable
	/// </summary>
	public readonly string[] DirectorySegments => this.FullSegments.Length > 1 ? this.FullSegments[..^1] : [];

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
		Array.Resize(ref pathSegments, pathSegments.Length + 1);
		pathSegments[^1] = "";
		return new(pathSegments);
	}
	public static VirtualPath FromFile(string path)
	{
		string[] pathSegments = BreakIntoDirectoryPathsAndSanitize(path, false);
		return new(pathSegments);
	}
	public static VirtualPath FromDirectoryOrFile(string path)
	{
		string[] pathSegments = BreakIntoDirectoryPathsAndSanitize(path);
		return new(pathSegments);
	}

	public override string ToString()
	{
		return $"{string.Join('/', this.FullSegments)}";
	}

	public readonly VirtualPath AppendDirectory(params IEnumerable<string> segments)
	{
		return new(this.DirectorySegments.Concat(segments).Append(this.FileName).ToArray());
	}
	public readonly VirtualPath Append(params IEnumerable<string> segments)
	{
		if (this.HasFileName)
			return new(this.FullSegments.Concat(segments).ToArray());
		else return new(this.FullSegments.Take(this.FullSegments.Length - 1).Concat(segments).ToArray());
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
