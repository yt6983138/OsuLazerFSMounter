namespace OsuLazerFSMounter.FileSystem;
public class VirtualDirectory : IVirtualFileSystemObject
{
	private readonly List<VirtualDirectory> _subdirectories = [];
	private readonly List<VirtualFile> _files = [];

	public VirtualDirectory? Parent { get; internal set; }

	/// <summary>
	/// only used to determine the original database entry for this directory
	/// </summary>
	public Guid Identifier { get; set; }
	public string Name { get; set; }
	public bool HasBeenRenamed { get; set; } = false;
	public IReadOnlyList<VirtualDirectory> Subdirectories => this._subdirectories;
	public IReadOnlyList<VirtualFile> Files => this._files;

	public bool IsEmpty => this.Subdirectories.Count == 0 && this.Files.Count == 0;

	public VirtualDirectory(string name)
	{
		this.Name = name;
	}

	public VirtualPath GetFullPath()
	{
		List<string> pathSegments = [];
		VirtualDirectory? current = this;
		while (current is not null)
		{
			pathSegments.Add(current.Name);
			current = current.Parent;
		}
		// remove the root directory's name
		pathSegments.RemoveAt(pathSegments.Count - 1);
		pathSegments.Reverse();
		pathSegments.Add("");
		return new(pathSegments.ToArray());
	}

	public VirtualFile? FindFile(VirtualPath path, StringComparison comparer = StringComparison.Ordinal)
	{
		if (path.FullSegments.Length == 0) return null;

		if (!path.HasFileName)
			return null;

		return this.FindFileInternal(path.FullSegments, 0, comparer);
	}
	private VirtualFile? FindFileInternal(string[] paths, int index, StringComparison comparer)
	{
		if (index == paths.Length - 1)
		{
			return this.Files.FirstOrDefault(f => f.Name.Equals(paths[index], comparer));
		}
		else
		{
			VirtualDirectory? subdir = this.Subdirectories.FirstOrDefault(d => d.Name.Equals(paths[index], comparer));
			if (subdir is null)
				return null;
			return subdir.FindFileInternal(paths, index + 1, comparer);
		}
	}

	/// <summary>
	/// This method will only check using directory segments, and will not consider the file name at the end of the path, even if it exists
	/// </summary>
	/// <param name="path"></param>
	/// <returns></returns>
	public VirtualDirectory? FindDirectory(VirtualPath path, StringComparison comparer = StringComparison.Ordinal)
	{
		if (path.DirectorySegments.Length == 0 || path.DirectorySegments[0] == "")
			return this;

		return this.FindDirectoryInternal(path.DirectorySegments, 0, comparer);
	}
	private VirtualDirectory? FindDirectoryInternal(Span<string> paths, int index, StringComparison comparer)
	{
		string current = paths[index];
		if (index == paths.Length - 1)
		{
			return this.Subdirectories.FirstOrDefault(d => d.Name.Equals(current, comparer));
		}
		else
		{
			VirtualDirectory? subdir = this.Subdirectories.FirstOrDefault(d => d.Name.Equals(current, comparer));
			if (subdir is null)
				return null;
			return subdir.FindDirectoryInternal(paths, index + 1, comparer);
		}
	}

	public void RemoveAllFiles()
	{
		this._files.Clear();
	}
	public void RemoveAllDirectories()
	{
		this._subdirectories.Clear();
	}
	public void RemoveAll()
	{
		this._files.Clear();
		this._subdirectories.Clear();
	}

	public bool RemoveFile(VirtualPath path, StringComparison comparer = StringComparison.Ordinal)
	{
		if (path.FullSegments.Length == 0) return false;
		if (!path.HasFileName)
			throw new ArgumentException("Path must end with a file name, not a directory", nameof(path));

		return this.RemoveFileInternal(path.FullSegments, 0, comparer);
	}
	private bool RemoveFileInternal(string[] paths, int index, StringComparison comparer)
	{
		if (index == paths.Length - 1)
		{
			bool hasAny = this._files.Any(f => f.Name.Equals(paths[index], comparer));
			this._files.RemoveAll(f => f.Name.Equals(paths[index], comparer));
			return hasAny;
		}
		else
		{
			VirtualDirectory? subdir = this.Subdirectories.FirstOrDefault(d => d.Name.Equals(paths[index], comparer));
			if (subdir is null)
				return false;
			return subdir.RemoveFileInternal(paths, index + 1, comparer);
		}
	}

	/// <inheritdoc cref="FindDirectory(VirtualPath, StringComparison)"/>
	public bool RemoveDirectory(VirtualPath path, StringComparison comparer = StringComparison.Ordinal)
	{
		if (path.DirectorySegments.Length == 0)
			return false;
		return this.RemoveDirectoryInternal(path.DirectorySegments, 0, comparer);
	}
	private bool RemoveDirectoryInternal(Span<string> paths, int index, StringComparison comparer)
	{
		string current = paths[index];
		if (index == paths.Length - 1)
		{
			bool hasAny = this._subdirectories.Any(d => d.Name.Equals(current, comparer));
			this._subdirectories.RemoveAll(d => d.Name.Equals(current, comparer));
			return hasAny;
		}
		else
		{
			VirtualDirectory? subdir = this.Subdirectories.FirstOrDefault(d => d.Name.Equals(current, comparer));
			if (subdir is null)
				return false;
			return subdir.RemoveDirectoryInternal(paths, index + 1, comparer);
		}
	}

	/// <summary>
	/// The path is only for specifying the directory to put the file in, file name is ignored
	/// </summary>
	/// <param name="file"></param>
	/// <param name="path"></param>
	public void AddFile(VirtualFile file, VirtualPath? path = null)
	{
		VirtualPath pathOrEmpty = path ?? new("");
		if (pathOrEmpty.DirectorySegments.Length == 0)
		{
			this._files.Add(file);
			file.Parent = this;
			return;
		}

		this.AddInternal(pathOrEmpty.DirectorySegments, 0, file);
	}
	private VirtualFile AddInternal(Span<string> paths, int index, VirtualFile file)
	{
		if (index == paths.Length)
		{
			this._files.Add(file);
			file.Parent = this;
			return file;
		}
		else
		{
			string current = paths[index];
			VirtualDirectory? subdir = this.Subdirectories.FirstOrDefault(d => d.Name == current);
			if (subdir is null)
			{
				subdir = new VirtualDirectory(current)
				{
					Parent = this
				};
				this._subdirectories.Add(subdir);
			}
			return subdir.AddInternal(paths, index + 1, file);
		}
	}

	/// <summary>
	/// this will add the directory into the path specified by the path parameter, 
	/// eg. add dir "a" with path "b/c/" will create directory "a" inside directory "c", which is inside directory "b".
	/// 
	/// The path is only for specifying the directory to put the directory in, if the path ends with a file name, it will be ignored.
	/// </summary>
	/// <param name="dir"></param>
	/// <param name="path"></param>
	public void AddDirectory(VirtualDirectory dir, VirtualPath? path = null)
	{
		VirtualPath pathOrEmpty = path ?? new("");
		if (pathOrEmpty.DirectorySegments.Length == 0)
		{
			dir.Parent = this;
			this._subdirectories.Add(dir);
			return;
		}
		this.AddDirectoryInternal(pathOrEmpty.DirectorySegments, dir, 0);
	}
	public VirtualDirectory AddDirectory(VirtualPath path)
	{
		if (path.DirectorySegments.Length == 0)
			return this;

		return this.AddDirectoryInternal(path.DirectorySegments[..^1], new VirtualDirectory(path.DirectorySegments[^1]), 0);
	}
	private VirtualDirectory AddDirectoryInternal(Span<string> paths, VirtualDirectory dir, int index)
	{
		if (index == paths.Length)
		{
			VirtualDirectory? existing = this.Subdirectories.FirstOrDefault(d => d.Name == dir.Name);
			if (existing is not null)
				return existing;

			dir.Parent = this;
			this._subdirectories.Add(dir);
			return dir;
		}
		else
		{
			string current = paths[index];
			VirtualDirectory? subdir = this.Subdirectories.FirstOrDefault(d => d.Name == current);
			if (subdir is null)
			{
				subdir = new VirtualDirectory(current)
				{
					Parent = this
				};

				this._subdirectories.Add(subdir);
			}
			return subdir.AddDirectoryInternal(paths, dir, index + 1);
		}
	}
}
