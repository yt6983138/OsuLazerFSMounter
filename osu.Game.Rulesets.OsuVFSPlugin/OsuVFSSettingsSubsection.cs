using Microsoft.Extensions.Logging;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Overlays.Settings;
using osu.Game.Skinning;
using OsuLazerFSMounter;
using System.Runtime.CompilerServices;

namespace osu.Game.Rulesets.OsuVFSPlugin;
public partial class OsuVFSSettingsSubsection : RulesetSettingsSubsection
{
	private SettingsButtonV2 _unmountButton = null!;

	[Resolved]
	private RealmAccess RealmAccess { get; set; } = null!;
	[Resolved]
	private BeatmapManager BeatmapManager { get; set; } = null!;
	[Resolved]
	private SkinManager SkinManager { get; set; } = null!;
	[Resolved]
	private OsuGame Game { get; set; } = null!;
	[Resolved]
	private GameHost Host { get; set; } = null!;
	[Resolved]
	private IDialogOverlay DialogOverlay { get; set; } = null!;

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
	public OsuVFSRulesetService RulesetService => OsuVFSRulesetService.GetOrCreateInstance(
		this.RealmAccess,
		this.FileDirectory,
		this.BeatmapManager,
		this.SkinManager,
		this.Game,
		this.Host);
	private ILogger<OsuVFSSettingsSubsection> Logger
	{
		get
		{
			field ??= this.RulesetService.LoggerFactory.CreateLogger<OsuVFSSettingsSubsection>();
			return field;
		}
	} = null!;

	protected override LocalisableString Header => nameof(OsuVFS);

	public OsuVFSSettingsSubsection(Ruleset ruleset) : base(ruleset) { }

	protected override void LoadComplete()
	{
		base.LoadComplete();
		this._unmountButton.Enabled.Value = false;
	}

#pragma warning disable IDE1006 // Naming Styles
	[BackgroundDependencyLoader]
	private void load()
#pragma warning restore IDE1006 // Naming Styles
	{
		OsuVFSRulesetConfigManager config = (OsuVFSRulesetConfigManager)this.Config;

		FormCheckBox readonlyCheckbox = new()
		{
			Caption = "Readonly",
			Current = config.GetBindable<bool>(OsuVFSRulesetOptions.ReadOnly),
			HintText = "Warning: This is HIGHLY EXPERIMENTAL and may cause data loss. Only disable this if you know what you're doing.",
		};
		FormTextBox mountPointTextbox = new()
		{
			Caption = "Mount Point",
			Current = config.GetBindable<string>(OsuVFSRulesetOptions.MountPoint),
			HintText = @"The path to mount the VFS at. (e.g. C:\VFS, F:, or leave empty to let the system choose)",
		};

		readonlyCheckbox.ValueChanged += () =>
		{
			if (readonlyCheckbox.Current.Value == true) return;

			this.DialogOverlay.Push(new ConfirmDialog("Are you sure you want to enable read-write mode? It is a HIGHLY EXPERIMENTAL feature and may cause data loss.", () =>
			{
				readonlyCheckbox.Current.Value = false;
			}, () => readonlyCheckbox.Current.Value = true));
		};

		SettingsButtonV2 mountButton = new()
		{
			Width = 0.5f,
			RelativeSizeAxes = Axes.X,
			Text = "Mount"
		};
		this._unmountButton = new()
		{
			Width = 0.5f,
			RelativeSizeAxes = Axes.X,
			Text = "Unmount",
		};

		mountButton.Action = () =>
		{
			mountButton.Enabled.Value = false;
			this.RulesetService.Options = new(
				config.Get<string>(OsuVFSRulesetOptions.MountPoint),
				config.Get<bool>(OsuVFSRulesetOptions.ReadOnly));
			try
			{
				this.RulesetService.Mount();
				this._unmountButton.Enabled.Value = true;
				mountPointTextbox.Current.Disabled = true;
				readonlyCheckbox.Current.Disabled = true;
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Failed to mount VFS.");

				mountButton.Enabled.Value = true;
				mountPointTextbox.Current.Disabled = false;
				readonlyCheckbox.Current.Disabled = false;
			}
		};
		this._unmountButton.Action = () =>
		{
			this._unmountButton.Enabled.Value = false;
			try
			{
				this.RulesetService.Unmount();
				mountButton.Enabled.Value = true;
				mountPointTextbox.Current.Disabled = false;
				readonlyCheckbox.Current.Disabled = false;
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Failed to unmount VFS.");
				this._unmountButton.Enabled.Value = true;
				mountPointTextbox.Current.Disabled = true;
				readonlyCheckbox.Current.Disabled = true;
			}
		};

		this.Children =
		[
			new SettingsItemV2(readonlyCheckbox),
			new SettingsItemV2(mountPointTextbox),
			new FillFlowContainer
			{
				RelativeSizeAxes = Axes.X,
				AutoSizeAxes = Axes.Y,
				Direction = FillDirection.Horizontal,
				Children =
				[
					mountButton,
					this._unmountButton
				]
			},
		];
	}
}
