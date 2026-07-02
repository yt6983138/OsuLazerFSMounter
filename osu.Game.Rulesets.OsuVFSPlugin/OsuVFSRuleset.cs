using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Logging;
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
using System.Runtime.InteropServices;

namespace osu.Game.Rulesets.OsuVFSPlugin;
public partial class OsuVFSRuleset : Ruleset
{
	public bool IsSupported { get; } = true;

	public override string Description => "OsuVFS as a ruleset plugin";
	public override string ShortName => nameof(OsuVFS);

	public OsuVFSRuleset()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			this.IsSupported = false;
			Logger.Log("OsuVFS is only supported on windows, settings will not be available.", level: LogLevel.Important);
		}
	}

	public override IRulesetConfigManager? CreateConfig(SettingsStore? settings)
	{
		if (!this.IsSupported)
			return null;

		return new OsuVFSRulesetConfigManager(settings, this.RulesetInfo);
	}
	public override RulesetSettingsSubsection? CreateSettings()
	{
		if (!this.IsSupported)
			return null;

		return new OsuVFSSettingsSubsection(this);
	}

	public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0)
	{
		if (!this.IsSupported)
			return [];

		return [
			new(keys: new(InputKey.Control, InputKey.Alt, InputKey.Shift, InputKey.S), OsuVFSKeyBind.ReloadSkin)
		];
	}

	public override Drawable CreateIcon()
	{
		if (!this.IsSupported)
			return new SpriteIcon { Icon = FontAwesome.Solid.QuestionCircle, Colour = Colour4.Transparent };

		return new OsuVFSIconInjection(this);
	}

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
