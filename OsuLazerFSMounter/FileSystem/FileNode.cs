namespace OsuLazerFSMounter.FileSystem;

public class FileNode
{
	public string[] SplitPath { get; set; }
	public VirtualFile File { get; set; }
	public FileStream Stream { get; set; }
	public FileInfo Info { get; set; }

	public FileNode(string[] splitPath, VirtualFile file, FileStream stream, FileInfo info)
	{
		this.SplitPath = splitPath;
		this.File = file;
		this.Stream = stream;
		this.Info = info;
	}
}
