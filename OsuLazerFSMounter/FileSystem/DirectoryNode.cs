namespace OsuLazerFSMounter.FileSystem;

public class DirectoryNode
{
	public string[] SplitPath { get; set; }
	public VirtualDirectory Directory { get; set; }

	public DirectoryNode(string[] splitPath, VirtualDirectory directory)
	{
		this.SplitPath = splitPath;
		this.Directory = directory;
	}
}
