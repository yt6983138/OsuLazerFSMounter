using osu.Framework.Input;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.OsuVFSPlugin.Dummy;
public partial class DummyDrawableRuleset : DrawableRuleset<HitObject>
{
	public DummyDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod> mods = null!) : base(ruleset, beatmap, mods)
	{
	}

	public override DrawableHitObject<HitObject> CreateDrawableRepresentation(HitObject h)
		=> new DummyDrawableHitObject(h);
	protected override PassThroughInputManager CreateInputManager()
		=> [];
	protected override Playfield CreatePlayfield()
		=> new DummyPlayField();
}
