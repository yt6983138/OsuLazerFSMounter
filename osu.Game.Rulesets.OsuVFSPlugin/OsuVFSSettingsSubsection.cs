using Microsoft.Extensions.Logging;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using OsuLazerFSMounter;
using System.Runtime.CompilerServices;

namespace osu.Game.Rulesets.OsuVFSPlugin;
public partial class OsuVFSSettingsSubsection : RulesetSettingsSubsection
{
	[Resolved]
	private RealmAccess RealmAccess { get; set; } = null!;
	private DirectoryInfo FileDirectory
	{
		get
		{
			Storage storage = GetStorage(this.RealmAccess);
			return new(storage.GetFullPath("files"));

			// i prob shouldnt do this
			[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "storage")]
			static extern ref Storage GetStorage(RealmAccess realmAccess);
		}
	}
	public OsuVFSRulesetService RulesetService => OsuVFSRulesetService.GetOrCreateInstance(this.RealmAccess, this.FileDirectory);
	private ILogger<OsuVFSSettingsSubsection> Logger
	{
		get
		{
			field ??= this.RulesetService.LoggerFactory.CreateLogger<OsuVFSSettingsSubsection>();
			return field;
		}
	} = null!;

	protected override LocalisableString Header => nameof(OsuVFS);

	public OsuVFSSettingsSubsection(Ruleset ruleset) : base(ruleset)
	{
	}

#pragma warning disable IDE1006 // Naming Styles
	[BackgroundDependencyLoader]
	private void load()
#pragma warning restore IDE1006 // Naming Styles
	{
		OsuVFSRulesetConfigManager config = (OsuVFSRulesetConfigManager)this.Config;

		SettingsButtonV2 mountButton = new()
		{
			Width = 0.5f,
			RelativeSizeAxes = Axes.X,
			Text = "Mount"
		};
		SettingsButtonV2 unmountButton = new()
		{
			Width = 0.5f,
			RelativeSizeAxes = Axes.X,
			Text = "Unmount",
			Enabled = { Value = false }
		};
		mountButton.Action = () =>
		{
			mountButton.Enabled.Value = false;
			this.RulesetService.Options = new(
				config.Get<OsuVFSMountPoint>(OsuVFSRulesetOptions.MountPoint),
				config.Get<bool>(OsuVFSRulesetOptions.ReadOnly));
			try
			{
				this.RulesetService.Mount();
				unmountButton.Enabled.Value = true;
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Failed to mount VFS.");

				mountButton.Enabled.Value = true;
			}
		};
		unmountButton.Action = () =>
		{
			unmountButton.Enabled.Value = false;
			try
			{
				this.RulesetService.Unmount();
				mountButton.Enabled.Value = true;
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Failed to unmount VFS.");
				unmountButton.Enabled.Value = true;
			}
		};

		this.Children =
		[
			new SettingsItemV2(new FormCheckBox()
			{
				Caption = "Readonly",
				Current = config.GetBindable<bool>(OsuVFSRulesetOptions.ReadOnly),
			}),
			new SettingsItemV2(new FormEnumDropdown<OsuVFSMountPoint>()
			{
				Caption = "Mount Point",
				Current = config.GetBindable<OsuVFSMountPoint>(OsuVFSRulesetOptions.MountPoint)
			}),
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Horizontal,
				Children =
				[
					mountButton,
					unmountButton
				]
			},
		];
	}
}
