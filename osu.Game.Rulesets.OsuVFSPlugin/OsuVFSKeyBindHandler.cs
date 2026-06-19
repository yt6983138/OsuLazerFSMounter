using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Database;
using osu.Game.Skinning;
using OsuLazerFSMounter.Utility;

namespace osu.Game.Rulesets.OsuVFSPlugin;
public partial class OsuVFSKeyBindHandler : Drawable, IKeyBindingHandler<OsuVFSKeyBind>
{
	private readonly SkinManager _skinManager;
	private readonly RealmAccess _realm;

	private readonly Live<SkinInfo> _classicSkin;
	private readonly Live<SkinInfo> _argonSkin;

	public OsuVFSKeyBindHandler(SkinManager skinManager, RealmAccess realm)
	{
		this._skinManager = skinManager;
		this._realm = realm;

		// those guid should be fixed and will likely never change
		this._classicSkin = this._realm.Run(x => x.Find<SkinInfo>(new Guid("81F02CD3-EEC6-4865-AC23-FAE26A386187")))
			.ThrowIfNull()
			.ToLive(realm);
		this._argonSkin = this._realm.Run(x => x.Find<SkinInfo>(new Guid("CFFA69DE-B3E3-4DEE-8563-3C4F425C05D0")))
			.ThrowIfNull()
			.ToLive(realm);
	}

	public bool OnPressed(KeyBindingPressEvent<OsuVFSKeyBind> e)
	{
		switch (e.Action)
		{
			case OsuVFSKeyBind.ReloadSkin:
				// not the best way to reload skin imo but easiest to do
				Live<SkinInfo> tempSkin = this._classicSkin;
				Live<SkinInfo> currentSkin = this._skinManager.CurrentSkinInfo.Value;

				if (currentSkin.Value.ID == this._classicSkin.Value.ID)
				{
					tempSkin = this._argonSkin;
				}

				this._skinManager.CurrentSkinInfo.Value = tempSkin;
				this._skinManager.CurrentSkinInfo.Value = currentSkin;
				break;
			default:
				return false;
		}

		return true;
	}
	public void OnReleased(KeyBindingReleaseEvent<OsuVFSKeyBind> e)
	{
	}
}
