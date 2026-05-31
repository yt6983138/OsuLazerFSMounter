namespace OsuLazerFSMounter.FileSystem;
public struct FileAttributeBuilder
{
	public FileAttributes Attributes { get; set; }
	public readonly uint UIntValue => (uint)this.Attributes;
	public readonly int IntValue => (int)this.Attributes;

	public FileAttributeBuilder(
		bool @readonly = false,
		bool hidden = false,
		bool system = false,
		bool directory = false,
		bool archive = false,
		bool device = false,
		bool normal = false,
		bool temporary = false,
		bool sparseFile = false,
		bool reparsePoint = false,
		bool compressed = false,
		bool offline = false,
		bool notContentIndexed = false,
		bool encrypted = false,
		bool integrityStream = false,
		bool noScrubData = false)
	{
		this.Attributes |= (FileAttributes)AsUInt(@readonly, 0);
		this.Attributes |= (FileAttributes)AsUInt(hidden, 1);
		this.Attributes |= (FileAttributes)AsUInt(system, 2);
		this.Attributes |= (FileAttributes)AsUInt(directory, 4);
		this.Attributes |= (FileAttributes)AsUInt(archive, 5);
		this.Attributes |= (FileAttributes)AsUInt(device, 6);
		this.Attributes |= (FileAttributes)AsUInt(normal, 7);
		this.Attributes |= (FileAttributes)AsUInt(temporary, 8);
		this.Attributes |= (FileAttributes)AsUInt(sparseFile, 9);
		this.Attributes |= (FileAttributes)AsUInt(reparsePoint, 10);
		this.Attributes |= (FileAttributes)AsUInt(compressed, 11);
		this.Attributes |= (FileAttributes)AsUInt(offline, 12);
		this.Attributes |= (FileAttributes)AsUInt(notContentIndexed, 13);
		this.Attributes |= (FileAttributes)AsUInt(encrypted, 14);
		this.Attributes |= (FileAttributes)AsUInt(integrityStream, 15);
		this.Attributes |= (FileAttributes)AsUInt(noScrubData, 16);
	}
	public FileAttributeBuilder(uint attribute)
	{
		this.Attributes = (FileAttributes)attribute;
	}
	public FileAttributeBuilder(int attribute)
		: this(unchecked((uint)attribute))
	{

	}

	private static uint AsUInt(bool value, int bitPosition)
		=> (uint)(value ? 1 : 0) << bitPosition;
}
