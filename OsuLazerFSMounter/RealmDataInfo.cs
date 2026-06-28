using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Skinning;
using OsuLazerFSMounter.Utility;

namespace OsuLazerFSMounter;

public record struct RealmFileInfo(string Hash, string Path)
{
	public RealmFileInfo(RealmNamedFileUsage file)
		: this(file.File.Hash, file.Filename) { }
}

public interface IOfflineRealmInfo
{
	Guid ID { get; }
	RealmFileInfo[] Files { get; }

	string GetDirectoryName();
}
public record class PartialSkinInfo(Guid ID, string Name, RealmFileInfo[] Files) : IOfflineRealmInfo
{
	public PartialSkinInfo(SkinInfo info)
		: this(info.ID, info.Name, info.Files.ToRealmInfos().ToArray()) { }

	public string GetDirectoryName()
	{
		return $"{Helper.SanitizeFileName(this.Name)} {this.ID.GetHashCode()}";
	}
}
public record class PartialBeatmapSetInfo(string Title, Guid ID, int OnlineID, RealmFileInfo[] Files) : IOfflineRealmInfo
{
	public PartialBeatmapSetInfo(BeatmapSetInfo info)
		: this(info.Metadata.Title, info.ID, info.OnlineID, info.Files.ToRealmInfos().ToArray()) { }

	public string GetDirectoryName()
	{
		if (this.OnlineID <= 0) return $"{Helper.SanitizeFileName(this.Title)} {this.ID.GetHashCode()}";
		else return $"{this.OnlineID} {Helper.SanitizeFileName(this.Title)}";
	}
}
