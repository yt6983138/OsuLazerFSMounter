using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.OsuVFSPlugin;
public partial class OsuVFSIconInjection : SpriteIcon
{
	private readonly Ruleset _ruleset;

	public OsuVFSIconInjection(Ruleset ruleset)
	{
		this._ruleset = ruleset;
		this.Icon = FontAwesome.Solid.QuestionCircle;
		this.Colour = Colour4.Transparent;
	}

	[Resolved]
	private RealmAccess RealmAccess { get; set; } = null!;
	[Resolved]
	private SkinManager SkinManager { get; set; } = null!;
	[Resolved]
	private OsuGame Game { get; set; } = null!;
	[Resolved]
	private IAPIProvider API { get; set; } = null!;

	protected override void LoadComplete()
	{
		base.LoadComplete();

		if (this.API.IsLoggedIn)
		{
			this.API.Logout();
			Logger.Log($"You have been logged out by {nameof(OsuVFSRuleset)} to prevent potential issues.", level: LogLevel.Important);
		}

		DatabasedKeyBindingContainer<OsuVFSKeyBind> container = new(this._ruleset.RulesetInfo, 0)
		{
			new OsuVFSKeyBindHandler(this.SkinManager, this.RealmAccess)
		};
		this.Game.Add(container);
	}
}
