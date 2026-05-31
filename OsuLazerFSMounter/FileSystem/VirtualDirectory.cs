namespace OsuLazerFSMounter.FileSystem;
public class VirtualDirectory
{
	public string Name { get; set; }
	public List<VirtualDirectory> Subdirectories { get; set; } = [];
	public List<VirtualFile> Files { get; set; } = [];

	public bool IsEmpty => this.Subdirectories.Count == 0 && this.Files.Count == 0;

	public VirtualDirectory(string name)
	{
		this.Name = name;
	}

	/// <summary>
	/// last one is always file name, string.Empty if path ends with a separator. ignores empty segments and trims whitespace from segments.
	/// this also trims starting slashes, so the first segment is always a directory name or file name
	/// </summary>
	/// <param name="path"></param>
	/// <returns></returns>
	public static string[] BreakIntoDirectoryPathsAndSanitize(string path, bool doNotEndWithSeparator = true)
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

		if (doNotEndWithSeparator && result[^1] == "")
			result = result[..^1];

		return result;
	}
	/// <summary>
	/// makes sure last element is the directory name
	/// </summary>
	/// <param name="paths"></param>
	/// <returns></returns>
	public static string[] EnsureLastIsDirectory(string[] paths)
	{
		if (paths.Length == 0)
			return [];
		if (!string.IsNullOrEmpty(paths[^1]))
			return paths;
		return paths.SkipLast(1).ToArray();
	}

	public VirtualFile? FindFile(string path)
	{
		string[] paths = BreakIntoDirectoryPathsAndSanitize(path);

		if (paths.Length == 0) return null;

		if (string.IsNullOrEmpty(paths[^1]))
			throw new ArgumentException("Path must end with a file name, not a directory", nameof(path));

		return this.FindFileInternal(paths, 0);
	}
	private VirtualFile? FindFileInternal(string[] paths, int index)
	{
		if (index == paths.Length - 1)
		{
			return this.Files.FirstOrDefault(f => f.Name == paths[index]);
		}
		else
		{
			VirtualDirectory? subdir = this.Subdirectories.FirstOrDefault(d => d.Name == paths[index]);
			if (subdir is null)
				return null;
			return subdir.FindFileInternal(paths, index + 1);
		}
	}

	public VirtualDirectory? FindDirectory(string path)
	{
		string[] paths = BreakIntoDirectoryPathsAndSanitize(path);

		paths = EnsureLastIsDirectory(paths);
		if (paths.Length == 0)
			return this;

		return this.FindDirectoryInternal(paths, 0);
	}
	private VirtualDirectory? FindDirectoryInternal(string[] paths, int index)
	{
		if (index == paths.Length - 1)
		{
			return this.Subdirectories.FirstOrDefault(d => d.Name == paths[index]);
		}
		else
		{
			VirtualDirectory? subdir = this.Subdirectories.FirstOrDefault(d => d.Name == paths[index]);
			if (subdir is null)
				return null;
			return subdir.FindDirectoryInternal(paths, index + 1);
		}
	}

	public VirtualFile AddFile(string path, string hash)
	{
		string[] paths = BreakIntoDirectoryPathsAndSanitize(path);

		return this.AddInternal(paths, 0, hash);
	}
	private VirtualFile AddInternal(string[] paths, int index, string hash)
	{
		if (index == paths.Length - 1)
		{
			VirtualFile file = new(paths[index], hash);
			this.Files.Add(file);
			return file;
		}
		else
		{
			VirtualDirectory? subdir = this.Subdirectories.FirstOrDefault(d => d.Name == paths[index]);
			if (subdir is null)
			{
				subdir = new VirtualDirectory(paths[index]);
				this.Subdirectories.Add(subdir);
			}
			return subdir.AddInternal(paths, index + 1, hash);
		}
	}

	public VirtualDirectory AddDirectory(string path)
	{
		string[] paths = BreakIntoDirectoryPathsAndSanitize(path);
		paths = EnsureLastIsDirectory(paths);

		if (paths.Length == 0)
			return this;

		return this.AddDirectoryInternal(paths, 0);
	}
	private VirtualDirectory AddDirectoryInternal(string[] paths, int index)
	{
		if (index == paths.Length - 1)
		{
			VirtualDirectory? existing = this.Subdirectories.FirstOrDefault(d => d.Name == paths[index]);
			if (existing is not null)
				return existing;

			VirtualDirectory dir = new(paths[index]);
			this.Subdirectories.Add(dir);
			return dir;
		}
		else
		{
			VirtualDirectory? subdir = this.Subdirectories.FirstOrDefault(d => d.Name == paths[index]);
			if (subdir is null)
			{
				subdir = new VirtualDirectory(paths[index]);
				this.Subdirectories.Add(subdir);
			}
			return subdir.AddDirectoryInternal(paths, index + 1);
		}
	}

	public VirtualDirectory? GetPathParent(string path)
	{
		string[] paths = BreakIntoDirectoryPathsAndSanitize(path);
		paths = EnsureLastIsDirectory(paths);

		if (paths.Length == 0)
			throw new ArgumentException("Path must not be pointing to the top-most directory", nameof(path));
		if (paths.Length == 1)
			return this;

		return this.FindDirectoryInternal(paths[..^1], 0);
	}
}
