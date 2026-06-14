using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.OsuVFSPlugin.Dummy;
public class DummyBeatmapConverter<T> : BeatmapConverter<T>
	where T : HitObject
{
	public DummyBeatmapConverter(IBeatmap beatmap, Ruleset ruleset) : base(beatmap, ruleset)
	{
	}

	public override bool CanConvert()
	{
		return false;
	}
}
