using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Monocle;

namespace Celeste.Mod.CompactSaveDeck;

public sealed class CompactSaveDeckModule : EverestModule
{
    public static CompactSaveDeckModule Instance { get; private set; } = null!;
    public static CompactSaveDeckSettings Settings => (CompactSaveDeckSettings)Instance._Settings;
    public override Type SettingsType => typeof(CompactSaveDeckSettings);
    private readonly Dictionary<OuiFileSelect, DeckController> controllers = new();
    private readonly Dictionary<OuiFileSelectSlot, DeckController> slotOwners = new();
    private Hook? idleHook;
    private bool entryHookInstalled;
    private bool menuHooksInstalled;
    public CompactSaveDeckModule() => Instance = this;

    public override void Load()
    {
        // A native event subscription does not construct a MonoMod detour or rewrite a method.
        // Keep the splash -> black screen -> snow loader path free of our menu hooks.
        Everest.Events.Celeste.OnSceneTransition += OnSceneTransition;
        Logger.Log(LogLevel.Info, "CompactSaveDeck", "Native scene event registered; no startup detours installed.");
    }

    public override void Initialize()
    {
        base.Initialize();
        // Also support enabling/reloading the module after the overworld already exists.
        if (Engine.Scene is Overworld) EnsureEntryHook();
    }

    private void OnSceneTransition(Scene previous, Scene next)
    {
        if (next is Overworld) EnsureEntryHook();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EnsureEntryHook()
    {
        if (entryHookInstalled) return;
        var timer = Stopwatch.StartNew();
        On.Celeste.OuiFileSelect.Enter += Enter;
        entryHookInstalled = true;
        Logger.Log(LogLevel.Info, "CompactSaveDeck", $"Overworld reached; entry hook initialized in {timer.Elapsed.TotalMilliseconds:F1} ms after the startup loader.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EnsureMenuHooks()
    {
        if (menuHooksInstalled) return;
        var timer = Stopwatch.StartNew();
        menuHooksInstalled = true;
        On.Celeste.OuiFileSelect.Leave += Leave;
        On.Celeste.OuiFileSelect.Update += Update;
        On.Celeste.OuiFileSelect.UnselectHighlighted += Unselect;
        On.Celeste.OuiFileSelectSlot.Show += ShowSlot;
        On.Celeste.OuiFileSelectSlot.Update += UpdateSlot;
        On.Celeste.OuiFileSelectSlot.Render += RenderSlot;
        idleHook = new Hook(typeof(OuiFileSelectSlot).GetProperty(nameof(OuiFileSelectSlot.IdlePosition))!.GetMethod!,
            (Func<Func<OuiFileSelectSlot, Vector2>, OuiFileSelectSlot, Vector2>)IdlePosition);
        Logger.Log(LogLevel.Info, "CompactSaveDeck", $"Save-menu hooks initialized in {timer.Elapsed.TotalMilliseconds:F1} ms on first entry.");
    }

    public override void Unload()
    {
        Everest.Events.Celeste.OnSceneTransition -= OnSceneTransition;
        if (entryHookInstalled)
        {
            On.Celeste.OuiFileSelect.Enter -= Enter;
            entryHookInstalled = false;
        }
        if (menuHooksInstalled)
        {
            On.Celeste.OuiFileSelect.Leave -= Leave;
            On.Celeste.OuiFileSelect.Update -= Update;
            On.Celeste.OuiFileSelect.UnselectHighlighted -= Unselect;
            On.Celeste.OuiFileSelectSlot.Show -= ShowSlot;
            On.Celeste.OuiFileSelectSlot.Update -= UpdateSlot;
            On.Celeste.OuiFileSelectSlot.Render -= RenderSlot;
            idleHook?.Dispose();
            idleHook = null;
            menuHooksInstalled = false;
        }
        foreach (var controller in controllers.Values) controller.Dispose();
        controllers.Clear();
        slotOwners.Clear();
    }

    internal void RegisterSlots(DeckController controller)
    {
        foreach (var slot in controller.Owner.Slots)
            if (slot != null) slotOwners[slot] = controller;
    }

    private IEnumerator Enter(On.Celeste.OuiFileSelect.orig_Enter orig, OuiFileSelect self, Oui from)
    {
        if (!controllers.TryGetValue(self, out var controller) && Settings.Enabled)
        {
            // A new Overworld owns fresh slots; release references to the previous one.
            foreach (var previous in controllers.Values) previous.Dispose();
            controllers.Clear();
            slotOwners.Clear();
            controllers[self] = controller = new DeckController(self);
        }
        if (controller == null) { yield return orig(self, from); yield break; }
        EnsureMenuHooks();
        controller.BeginEnter();
        // Vanilla requires a real slot index even while our focused item is a deck header.
        self.SlotIndex = Math.Max(0, self.SlotIndex);
        IEnumerator routine = orig(self, from);
        try
        {
            while (routine.MoveNext())
            {
                if (!controller.Ready && OuiFileSelect.Loaded)
                    controller.Prepare();
                yield return routine.Current;
            }
        }
        finally { (routine as IDisposable)?.Dispose(); }
        controller.Prepare();
        controller.EndEnter();
    }

    private IEnumerator Leave(On.Celeste.OuiFileSelect.orig_Leave orig, OuiFileSelect self, Oui next)
    {
        if (!controllers.TryGetValue(self, out var controller)) { yield return orig(self, next); yield break; }
        controller.BeginLeave(next);
        IEnumerator routine = orig(self, next);
        try
        {
            while (routine.MoveNext())
            {
                yield return routine.Current;
            }
        }
        finally { (routine as IDisposable)?.Dispose(); }
    }

    private void Update(On.Celeste.OuiFileSelect.orig_Update orig, OuiFileSelect self)
    {
        if (!controllers.TryGetValue(self, out var controller) || !controller.Ready)
        { orig(self); return; }
        if (self.SlotSelected) { orig(self); controller.Tick(false); return; }
        bool focused = self.Focused;
        self.Focused = false;
        try { orig(self); }
        finally { self.Focused = focused; }
        controller.Tick(focused && self.Selected);
    }

    private void Unselect(On.Celeste.OuiFileSelect.orig_UnselectHighlighted orig, OuiFileSelect self)
    {
        orig(self);
        if (controllers.TryGetValue(self, out var controller)) controller.OnUnselect();
    }

    private Vector2 IdlePosition(Func<OuiFileSelectSlot, Vector2> orig, OuiFileSelectSlot self)
        => slotOwners.TryGetValue(self, out var controller) && controller.Ready
            ? controller.IdlePositionFor(self.FileSlot) : orig(self);

    private void ShowSlot(On.Celeste.OuiFileSelectSlot.orig_Show orig, OuiFileSelectSlot self)
    {
        orig(self);
        if (slotOwners.TryGetValue(self, out var controller)) controller.OnNativeShow();
    }

    private void UpdateSlot(On.Celeste.OuiFileSelectSlot.orig_Update orig, OuiFileSelectSlot self)
    {
        if (!slotOwners.TryGetValue(self, out var controller) || !controller.Ready)
        { orig(self); return; }
        if (controller.Owner.SlotSelected)
        {
            if (controller.Owner.SlotIndex == self.FileSlot) orig(self);
            else self.Visible = false;
            return;
        }
        if (!controller.IsVisible(self.FileSlot)) { self.Visible = false; return; }
        self.Visible = true;
        orig(self);
        self.Position = controller.PositionFor(self.FileSlot);
        // Native Hide may finish before a later visible row in our shared cascade.
        self.Visible = controller.IsVisible(self.FileSlot);
    }

    private void RenderSlot(On.Celeste.OuiFileSelectSlot.orig_Render orig, OuiFileSelectSlot self)
    {
        if (slotOwners.TryGetValue(self, out var controller) && controller.Ready && !controller.Owner.SlotSelected)
        {
            if (!controller.IsVisible(self.FileSlot)) return;
            self.Position = controller.PositionFor(self.FileSlot);
        }
        orig(self);
    }
}

