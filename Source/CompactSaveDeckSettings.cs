using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.CompactSaveDeck;

public sealed class CompactSaveDeckSettings : EverestModuleSettings
{
    [SettingName("csd_enabled")]
    [SettingSubText("csd_controls_help")]
    public bool Enabled { get; set; } = true;

    [SettingName("csd_manage")]
    [DefaultButtonBinding(Buttons.LeftTrigger, Keys.Tab)]
    public ButtonBinding ManageDecks { get; set; } = new(Buttons.LeftTrigger, Keys.Tab);


    [SettingName("csd_expand")]
    [DefaultButtonBinding(Buttons.DPadRight, Keys.Right)]
    public ButtonBinding ExpandDeck { get; set; } = new(Buttons.DPadRight, Keys.Right);

    [SettingName("csd_collapse")]
    [DefaultButtonBinding(Buttons.DPadLeft, Keys.Left)]
    public ButtonBinding CollapseDeck { get; set; } = new(Buttons.DPadLeft, Keys.Left);
}
