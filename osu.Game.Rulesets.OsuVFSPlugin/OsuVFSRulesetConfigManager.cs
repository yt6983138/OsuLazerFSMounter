using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.OsuVFSPlugin;

public enum OsuVFSRulesetOptions
{
	ReadOnly,
	MountPoint
}
public class OsuVFSRulesetConfigManager : RulesetConfigManager<OsuVFSRulesetOptions>
{
	public OsuVFSRulesetConfigManager(SettingsStore? store, RulesetInfo ruleset, int? variant = null)
		: base(store!, ruleset, variant) // goddamn it their nullabilities are inconsistent
	{
	}

	protected override void InitialiseDefaults()
	{
		this.SetDefault(OsuVFSRulesetOptions.ReadOnly, true);
		this.SetDefault(OsuVFSRulesetOptions.MountPoint, "");
	}
}
