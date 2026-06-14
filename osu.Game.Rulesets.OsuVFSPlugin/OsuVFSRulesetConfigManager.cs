using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.OsuVFSPlugin;

public enum OsuVFSRulesetOptions
{
	ReadOnly,
	MountPoint
}
public enum OsuVFSMountPoint
{
	Auto,
	// hmmm
	A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z
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
		this.SetDefault(OsuVFSRulesetOptions.MountPoint, OsuVFSMountPoint.Auto);
	}
}
