namespace OsuLazerFSMounter.FileSystem;

public class VirtualFile
{
	public string Name { get; set; }
	public string Hash { get; set; }

	public VirtualFile(string name, string hash)
	{
		this.Name = name;
		this.Hash = hash;
	}
}