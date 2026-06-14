using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.OsuVFSPlugin.Dummy;
using osu.Game.Rulesets.UI;
using OsuLazerFSMounter;

namespace osu.Game.Rulesets.OsuVFSPlugin;
public partial class OsuVFSRuleset : Ruleset
{
	public override string Description => "OsuVFS as a ruleset plugin";
	public override string ShortName => nameof(OsuVFS);

	public override IRulesetConfigManager CreateConfig(SettingsStore? settings)
	=> new OsuVFSRulesetConfigManager(settings, this.RulesetInfo);
	public override RulesetSettingsSubsection CreateSettings()
		=> new OsuVFSSettingsSubsection(this);

	#region Required dummy implementations

	public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap)
		=> new DummyBeatmapConverter<HitObject>(beatmap, this);
	public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap)
		=> new DummyDifficultyCalculator(this.RulesetInfo, beatmap);
	public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
		=> new DummyDrawableRuleset(this, beatmap, mods!);
	public override IEnumerable<Mod> GetModsFor(ModType type) => [];

	#endregion
}
