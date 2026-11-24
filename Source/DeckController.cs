using System.Collections;
using Celeste.Mod.CompactSaveDeck.Model;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.CompactSaveDeck;

internal sealed class DeckController : IDisposable
{
    internal OuiFileSelect Owner { get; }
    internal bool Ready { get; private set; }
    internal bool Leaving { get; private set; }
    private readonly DeckStore store;
    private DeckDocument document;
    private readonly List<Row> rows = new();
    private readonly Dictionary<int, Row> saveRows = new();
    private DeckOverlay? overlay;
    private bool prepared;
    private bool entering;
    private bool slotsHidden;
    private bool layoutAnimating;
    private MTexture? deckCardTexture;
    private readonly SavePageMotion pageMotion = new();
    private int focusedIndex;
    private float scroll, targetScroll;
    private string focusKey = "";
    private DeckMenuPanel? menu;
    private DeckDocument? reorderSnapshot;
    private string? movingKey;
    private string? toast;
    private float toastTime;
    private int inputCooldown;
    private readonly Dictionary<string, VisualRow> visualRows = new();
    private readonly Dictionary<int, VisualRow> visualSaves = new();
    private readonly List<string> retiredKeys = new();

    private sealed class VisualRow
    {
        public Row Row;
        public readonly CardMotion Motion;
        public bool Retiring;
        public VisualRow(Row row, CardPose pose) { Row = row; Motion = new(pose); }
    }

    private sealed record Row(string Key, int? Slot, Deck? Group, bool Header, float Center, float Height);
    private sealed record MenuItem(Func<string> Label, Action Action);
    private Row? Current => rows.Count == 0 ? null : rows[Math.Clamp(focusedIndex, 0, rows.Count - 1)];
    private static string T(string key) => Dialog.Clean("csd_" + key);

    internal DeckController(OuiFileSelect owner)
    {
        Owner = owner;
        store = new DeckStore(Path.Combine(UserIO.GetSaveFilePath(), "compact-save-deck.json"));
        document = store.Load();
        focusKey = document.Focus ?? "";
        if (store.Warning != null)
        {
            Logger.Log(LogLevel.Warn, "CompactSaveDeck", store.Warning);
            Toast(T("recovery"));
        }
    }

    internal void BeginEnter()
    {
        Leaving = false;
        Ready = false;
        prepared = false;
        entering = true;
        pageMotion.ArmEntry();
        slotsHidden = false;
    }

    internal void Prepare()
    {
        if (prepared || Owner.Scene == null || Owner.Slots.Any(s => s == null)) return;
        prepared = true;
        document.Reconcile(Owner.Slots.Where(s => s.Exists).Select(s => s.FileSlot));
        CompactSaveDeckModule.Instance.RegisterSlots(this);
        if (overlay == null || overlay.Scene != Owner.Scene)
        {
            overlay?.RemoveSelf();
            overlay = new DeckOverlay(this);
            Owner.Scene.Add(overlay);
        }
        Ready = true;
        Rebuild(animate: false);
        scroll = targetScroll;
    }

    internal void EndEnter()
    {
        if (!Ready) return;
        // Returning from naming / assist pages keeps vanilla's selected slot and actions.
        if (Owner.SlotSelected) pageMotion.Settle();
        else { ApplyFocus(); OnNativeShow(); }
        entering = false;
        Leaving = false;
        inputCooldown = 2;
    }

    internal void BeginLeave(Oui next)
    {
        Leaving = true;
        menu?.Dispose();
        menu = null;
        if (reorderSnapshot != null)
        {
            document = reorderSnapshot;
            reorderSnapshot = null;
            movingKey = null;
            Rebuild();
        }
        document.Focus = focusKey;
        Persist();
        int direction = next == null || next is OuiChapterSelect || next is OuiFileNaming ||
            next is OuiFileSelectSlot.ISubmenu || next is OuiAssistMode ? -1 : 1;
        pageMotion.Hide(VisiblePageKeys(), direction);
    }

    private IEnumerable<string> VisiblePageKeys() => visualRows.Values
        .Where(v => v.Motion.Current.Y - scroll >= -200f && v.Motion.Current.Y - scroll <= 1280f)
        .OrderBy(v => v.Motion.Current.Y).Select(v => v.Row.Key);

    internal void OnNativeShow()
    {
        if (entering && Ready && !Owner.SlotSelected)
            pageMotion.Show(VisiblePageKeys());
    }

    private void Rebuild(string? desiredFocus = null, bool animate = true)
    {
        if (desiredFocus != null) focusKey = desiredFocus;
        rows.Clear();
        saveRows.Clear();
        float top = 80f;
        void Add(string key, int? slot, Deck? group, bool header, float height)
        {
            var row = new Row(key, slot, group, header, top + height / 2f, height);
            rows.Add(row);
            if (slot.HasValue) saveRows[slot.Value] = row;
            top += height + 10f;
        }
        foreach (string key in document.RootOrder)
        {
            Deck? group = document.Find(key);
            if (group != null)
            {
                Add(key, null, group, true, group.Expanded ? 100f : 300f);
                if (group.Expanded)
                    foreach (int slot in group.Slots) Add(DeckDocument.SaveKey(slot), slot, group, false, 300f);
            }
            else if (key.StartsWith("s:") && int.TryParse(key.AsSpan(2), out int slot) &&
                     slot >= 0 && slot < Owner.Slots.Length && Owner.Slots[slot].Exists)
                Add(key, slot, null, false, 300f);
        }
        var empty = Owner.Slots.FirstOrDefault(s => !s.Exists);
        if (empty != null) Add(DeckDocument.SaveKey(empty.FileSlot), empty.FileSlot, null, false, 300f);
        int index = rows.FindIndex(r => r.Key == focusKey);
        focusedIndex = index >= 0 ? index : Math.Clamp(focusedIndex, 0, Math.Max(0, rows.Count - 1));
        ApplyFocus();
        Reflow(animate);
    }

    private void Reflow(bool animate)
    {
        var targets = rows.ToDictionary(row => row.Key);
        var oldPoses = visualRows.ToDictionary(pair => pair.Key, pair => pair.Value.Motion.Current);
        foreach (Row row in rows)
        {
            CardPose target = new(960f, row.Center, row.Header && row.Group!.Expanded ? 1f : 0f);
            float stagger = 0;
            if (!visualRows.TryGetValue(row.Key, out var visual))
            {
                CardPose origin = target;
                if (animate && row.Center - targetScroll >= -300f && row.Center - targetScroll <= 1380f && row.Slot.HasValue && row.Group != null &&
                    oldPoses.TryGetValue(DeckDocument.DeckKey(row.Group.Id), out var stack))
                {
                    int order = row.Group.Slots.IndexOf(row.Slot.Value);
                    origin = new CardPose(stack.X + 12f * Math.Min(order + 1, 2),
                        stack.Y + 9f * Math.Min(order + 1, 2));
                    stagger = Math.Min(order, 3) * 0.02f;
                }
                visualRows[row.Key] = visual = new VisualRow(row, origin);
            }
            visual.Row = row;
            visual.Retiring = false;
            visual.Motion.Retarget(target, stagger, !animate);
        }
        retiredKeys.Clear();
        foreach (var pair in visualRows)
        {
            if (targets.ContainsKey(pair.Key)) continue;
            VisualRow visual = pair.Value;
            Row row = visual.Row;
            if (animate && visual.Motion.Current.Y - scroll >= -300f && visual.Motion.Current.Y - scroll <= 1380f && row.Slot.HasValue && row.Group != null &&
                targets.TryGetValue(DeckDocument.DeckKey(row.Group.Id), out var header))
            {
                int order = Math.Max(0, row.Group.Slots.IndexOf(row.Slot.Value));
                visual.Retiring = true;
                visual.Motion.Retarget(new CardPose(960f + 12f * Math.Min(order + 1, 2),
                    header.Center + 9f * Math.Min(order + 1, 2)));
            }
            else retiredKeys.Add(pair.Key);
        }
        foreach (string key in retiredKeys) visualRows.Remove(key);
        layoutAnimating = visualRows.Values.Any(v => !v.Motion.Complete || v.Retiring);
        visualSaves.Clear();
        foreach (VisualRow visual in visualRows.Values)
            if (visual.Row.Slot is int slot) visualSaves[slot] = visual;
    }

    private void AdvanceMotion()
    {
        if (!layoutAnimating) return;
        layoutAnimating = false;
        retiredKeys.Clear();
        foreach (var pair in visualRows)
        {
            VisualRow visual = pair.Value;
            visual.Motion.Advance(Engine.DeltaTime);
            layoutAnimating |= !visual.Motion.Complete;
            if (visual.Retiring && visual.Motion.Complete) retiredKeys.Add(pair.Key);
        }
        foreach (string key in retiredKeys)
        {
            if (visualRows[key].Row.Slot is int slot) visualSaves.Remove(slot);
            visualRows.Remove(key);
        }
    }

    private void ApplyFocus()
    {
        Row? row = Current;
        if (row == null) return;
        focusKey = row.Key;
        document.Focus = focusKey;
        // FileSlot remains the real, immutable save identity; only the visual list is reordered.
        if (!Owner.SlotSelected) Owner.SlotIndex = row.Slot ?? -1;
        if (row.Group != null && row.Slot.HasValue) row.Group.LastSlot = row.Slot;
        float bottom = rows[^1].Center + rows[^1].Height / 2f;
        targetScroll = Math.Clamp(row.Center - 540f, 0f, Math.Max(0f, bottom - 1000f));
    }

    internal Vector2 IdlePositionFor(int slot) => visualSaves.TryGetValue(slot, out var visual)
        ? new Vector2(visual.Motion.Current.X, visual.Motion.Current.Y - scroll)
        : new Vector2(960f, -2500f);

    internal Vector2 PositionFor(int slot) => visualSaves.TryGetValue(slot, out var visual)
        ? new Vector2(visual.Motion.Current.X + pageMotion.Offset(visual.Row.Key), visual.Motion.Current.Y - scroll)
        : new Vector2(960f, -2500f);

    internal bool IsVisible(int slot)
    {
        // Reuse the row's stable key: this runs several times per visible card per frame.
        if (!Ready || !visualSaves.TryGetValue(slot, out var visual) || !pageMotion.CanDraw(visual.Row.Key)) return false;
        float y = visual.Motion.Current.Y - scroll;
        return y >= -200f && y <= 1280f;
    }
    internal void SyncSlotsForRender()
    {
        if (!Ready || Owner.SlotSelected || (!pageMotion.Drawing && slotsHidden)) return;
        // Show/Hide can run after a slot's Update, and Depth changes reorder entity updates.
        // Reconcile once after every update to give native cards and deck covers the same frame.
        foreach (OuiFileSelectSlot slot in Owner.Slots)
        {
            if (slot == null) continue;
            slot.Visible = IsVisible(slot.FileSlot);
            if (slot.Visible) slot.Position = PositionFor(slot.FileSlot);
        }
        slotsHidden = !pageMotion.Drawing;
    }
    internal void Tick(bool input)
    {
        if (!entering && !pageMotion.Drawing && menu == null) return;
        if (toastTime > 0) toastTime -= Engine.DeltaTime;
        pageMotion.Advance(Engine.DeltaTime);
        if (menu != null)
        {
            DeckMenuPanel activeMenu = menu;
            activeMenu.Update();
            if (activeMenu.Closed)
            {
                menu = null;
                activeMenu.Dispose();
                activeMenu.AfterClose?.Invoke();
                Consume();
            }
        }
        if (!Leaving)
        {
            AdvanceMotion();
            scroll = Calc.Approach(scroll, targetScroll,
                Math.Max(1000f, Math.Abs(targetScroll - scroll) * 12f) * Engine.DeltaTime);
        }
        if (Leaving || entering || !pageMotion.Interactive || !input || !Ready) return;
        if (inputCooldown > 0) { inputCooldown--; return; }

        if (menu != null) { menu.HandleInput(); return; }
        if (movingKey != null)
        {
            if (Input.MenuCancel.Pressed)
            {
                document = reorderSnapshot!;
                reorderSnapshot = null;
                string key = movingKey;
                movingKey = null;
                Rebuild(key);
                Consume();
            }
            else if (Input.MenuConfirm.Pressed)
            {
                DeckDocument before = reorderSnapshot!;
                reorderSnapshot = null;
                movingKey = null;
                if (!Persist()) document = before;
                Rebuild();
                Consume();
            }
            else if (Input.MenuUp.Pressed || Input.MenuDown.Pressed)
            {
                document.Move(movingKey, Input.MenuUp.Pressed ? -1 : 1);
                Rebuild(movingKey);
                Rollover();
            }
            return;
        }
        if (CompactSaveDeckModule.Settings.ManageDecks.Pressed)
        {
            CompactSaveDeckModule.Settings.ManageDecks.ConsumePress();
            OpenManager();
        }
        else if (MInput.Keyboard.Pressed(Keys.R))
        {
            OpenSearch();
        }
        else if (Input.MenuUp.Pressed || Input.MenuDown.Pressed)
        {
            if (rows.Count > 0)
            {
                focusedIndex = (focusedIndex + (Input.MenuUp.Pressed ? -1 : 1) + rows.Count) % rows.Count;
                ApplyFocus();
                Rollover();
            }
        }
        else if (CompactSaveDeckModule.Settings.ExpandDeck.Pressed && Current is { Header: true, Group: not null } right && !right.Group.Expanded)
            Toggle(right.Group);
        else if (CompactSaveDeckModule.Settings.CollapseDeck.Pressed && Current?.Group is Deck parent && parent.Expanded)
            Collapse(parent);
        else if (Input.MenuConfirm.Pressed && Current is Row row)
        {
            Input.MenuConfirm.ConsumePress();
            if (row.Header) Toggle(row.Group!);
            else
            {
                Owner.SlotIndex = row.Slot!.Value;
                document.Focus = row.Key;
                Persist();
                Audio.Play("event:/ui/main/button_select");
                Owner.SelectSlot(true);
            }
        }
        else if (Input.MenuCancel.Pressed)
        {
            Input.MenuCancel.ConsumePress();
            if (Current?.Group is Deck cancelGroup && cancelGroup.Expanded) Collapse(cancelGroup);
            else { Audio.Play("event:/ui/main/button_back"); Owner.Overworld.Goto<OuiMainMenu>(); }
        }
    }

    private void Toggle(Deck deck)
    {
        if (deck.Expanded) { Collapse(deck); return; }
        deck.Expanded = true;
        string key = deck.LastSlot.HasValue && deck.Slots.Contains(deck.LastSlot.Value)
            ? DeckDocument.SaveKey(deck.LastSlot.Value)
            : deck.Slots.Count > 0 ? DeckDocument.SaveKey(deck.Slots[0]) : DeckDocument.DeckKey(deck.Id);
        Rebuild(key);
        Rollover();
    }

    private void Collapse(Deck deck)
    {
        deck.Expanded = false;
        Rebuild(DeckDocument.DeckKey(deck.Id));
        Rollover();
    }

    internal void OnUnselect()
    {
        if (!Ready) return;
        int slot = Owner.SlotIndex;
        bool deleted = slot >= 0 && slot < Owner.Slots.Length && !Owner.Slots[slot].Exists &&
            (document.RootOrder.Contains(DeckDocument.SaveKey(slot)) || document.GroupOf(slot) != null);
        if (deleted)
        {
            document.Reconcile(Owner.Slots.Where(s => s.Exists).Select(s => s.FileSlot));
            Persist();
        }
        Rebuild(deleted ? null : DeckDocument.SaveKey(slot));
        inputCooldown = 2;
    }

    private static MenuItem Item(string key, Action action) => new(() => T(key), action);
    private void SetMenu(string title, IEnumerable<MenuItem> entries, Action? back = null)
    {
        var snapshot = entries.ToList();
        if (menu != null) { CloseMenu(() => SetMenu(title, snapshot, back)); return; }
        menu = new DeckMenuPanel(Owner.Scene, Owner.Overworld, title,
            snapshot.Select(item => (item.Label, item.Action)),
            () => CloseMenu(back));
        inputCooldown = 1;
    }

    private void CloseMenu(Action? next = null) => menu?.Close(next);

    private void OpenManager()
    {
        Row? row = Current;
        var entries = new List<MenuItem>();
        if (row is { Header: true, Group: not null })
        {
            Deck group = row.Group;
            entries.Add(Item("members", () => EditMembers(group)));
            entries.Add(Item("rename_deck", () => NamePrompt(group.Name, name =>
                Commit(() => group.Name = name))));
            entries.Add(Item("reorder", StartReorder));
            entries.Add(Item("dissolve", () => SetMenu(T("dissolve_confirm"), new[] {
                Item("cancel", OpenManager),
                Item("confirm", () => Commit(() => document.Dissolve(group.Id)))
            }, OpenManager)));
        }
        else if (row?.Slot is int slot && Owner.Slots[slot].Exists)
        {
            entries.Add(Item("reorder", StartReorder));
            entries.Add(Item("assign", () => ChooseDeck(slot)));
            if (row.Group != null) entries.Add(Item("ungroup", () => Commit(() => document.Assign(slot, null), DeckDocument.SaveKey(slot))));
            entries.Add(Item("create_from_here", () => NamePrompt(T("default_deck"), name =>
                Commit(() => { var d = document.Create(name, new[] { slot }); focusKey = DeckDocument.DeckKey(d.Id); }))));
        }
        entries.Add(Item("create_deck", () => EditMembers(null)));
        SetMenu(T("manage"), entries);
    }

    private void StartReorder()
    {
        if (menu != null) { CloseMenu(StartReorder); return; }
        reorderSnapshot = document.Clone();
        movingKey = Current!.Key;
        Consume();
    }
    private void ChooseDeck(int slot)
    {
        var entries = document.Decks.Select(deck => new MenuItem(() => deck.Name,
            () => Commit(() => document.Assign(slot, deck.Id), DeckDocument.DeckKey(deck.Id)))).ToList();
        entries.Add(Item("create_from_here", () => NamePrompt(T("default_deck"), name =>
            Commit(() => { var d = document.Create(name, new[] { slot }); focusKey = DeckDocument.DeckKey(d.Id); }))));
        SetMenu(T("assign"), entries, OpenManager);
    }

    private IEnumerable<int> OrderedSlots()
    {
        foreach (string key in document.RootOrder)
        {
            if (document.Find(key) is Deck deck)
            {
                foreach (int slot in deck.Slots) yield return slot;
            }
            else if (int.TryParse(key.AsSpan(2), out int slot)) yield return slot;
        }
    }

    private void EditMembers(Deck? group)
    {
        var selected = group?.Slots.ToHashSet() ?? new HashSet<int>();
        var ordered = OrderedSlots().ToList();
        var entries = new List<MenuItem>();
        entries.Add(new MenuItem(() => T("apply_members") + " (" + selected.Count + ")", () =>
        {
            if (group == null)
            {
                if (selected.Count == 0) { Toast(T("choose_one")); return; }
                NamePrompt(T("default_deck"), name => Commit(() =>
                {
                    var created = document.Create(name, ordered.Where(selected.Contains));
                    focusKey = DeckDocument.DeckKey(created.Id);
                }));
            }
            else Commit(() =>
            {
                foreach (int slot in group.Slots.ToList())
                    if (!selected.Contains(slot)) document.Assign(slot, null);
                foreach (int slot in ordered.Where(selected.Contains)) document.Assign(slot, group.Id);
            }, DeckDocument.DeckKey(group.Id));
        }));
        foreach (int slot in ordered)
        {
            int id = slot;
            entries.Add(new MenuItem(() => (selected.Contains(id) ? "[x] " : "[ ] ") +
                (Owner.Slots[id].Name ?? T("damaged")) + "  #" + id, () =>
            {
                if (!selected.Add(id)) selected.Remove(id);
            }));
        }
        SetMenu(T("members"), entries, OpenManager);
    }

    private void NamePrompt(string value, Action<string> accept)
    {
        if (menu != null) { CloseMenu(() => NamePrompt(value, accept)); return; }
        string edited = value;
        Owner.Overworld.Goto<OuiModOptionString>().Init(value, text => edited = text, confirm =>
        {
            if (confirm && !string.IsNullOrWhiteSpace(edited)) accept(edited.Trim());
            Owner.Overworld.Goto<OuiFileSelect>();
        }, 32, 1);
    }

    private string SaveMapLabel(int slot)
    {
        SaveData? data = Owner.Slots[slot].SaveData;
        if (data == null) return T("unknown_map");
        AreaData? area = AreaData.Get(data.LastArea_Safe);
        if (area == null) return T("unknown_map");
        // Use this slot's last area, not the globally active save or its furthest unlock.
        string levelSet = Dialog.CleanLevelSet(area.LevelSet);
        string chapter = area.Name.DialogCleanOrNull() ?? area.Name;
        return string.IsNullOrWhiteSpace(chapter) || chapter == levelSet
            ? levelSet : levelSet + " / " + chapter;
    }
    private void OpenSearch()
    {
        var catalog = DeckSearch.Catalog(document, slot => Owner.Slots[slot].Name ?? T("damaged"), SaveMapLabel);
        var entries = catalog.Select(entry => (
            Label: (Func<string>)(() => (entry.IsDeck ? T("deck_label") : T("save_label")) + "  " + entry.Name +
                (entry.Slot.HasValue ? "  —  " + entry.Map + "  " + entry.Context : "")),
            Action: (Action)(() => RevealSearchResult(entry))));
        menu = new DeckMenuPanel(Owner.Scene, Owner.Overworld, T("search"), entries,
            () => CloseMenu(), (query, panel) =>
            {
                int match = DeckSearch.BestIndex(catalog, query);
                if (match < 0)
                {
                    panel.Feedback = T("no_match");
                    Audio.Play("event:/ui/main/button_invalid");
                    return;
                }
                panel.Highlight(match);
                RevealSearchResult(catalog[match]);
            });
        inputCooldown = 1;
    }

    private void RevealSearchResult(SaveSearchEntry entry)
    {
        // Reveal a collapsed member without selecting its native Continue / New Game actions.
        if (entry.Slot is int slot && document.GroupOf(slot) is Deck group) group.Expanded = true;
        Rebuild(entry.Key);
    }
    private void Commit(Action change, string? focus = null)
    {
        DeckDocument before = document.Clone();
        string oldFocus = focusKey;
        try
        {
            change();
            if (!Persist()) { document = before; focusKey = oldFocus; }
            else if (focus != null) focusKey = focus;
        }
        catch (Exception ex)
        {
            document = before;
            focusKey = oldFocus;
            Report(ex);
        }
        CloseMenu();
        Rebuild();
        Consume();
    }

    private bool Persist()
    {
        try { store.Save(document); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Report(ex); return false; }
    }

    private void Report(Exception ex)
    {
        Logger.Log(LogLevel.Error, "CompactSaveDeck", ex.ToString());
        Toast(T("save_error"));
    }

    private void Toast(string message) { toast = message; toastTime = 8f; }
    private static void Rollover() => Audio.Play("event:/ui/main/rollover_down");
    private void Consume()
    {
        Input.MenuConfirm.ConsumePress();
        Input.MenuCancel.ConsumePress();
        inputCooldown = 2;
    }

    internal void Render()
    {
        // Overworld.Current is null during Enter/Leave; Next identifies the incoming page.
        if (!Ready || !pageMotion.Drawing ||
            (!Owner.Selected && Owner.Overworld.Next != Owner && !pageMotion.Exiting) || Owner.SlotSelected) return;
        foreach (VisualRow visual in visualRows.Values)
        {
            Row row = visual.Row;
            float y = visual.Motion.Current.Y - scroll;
            if (y < -200f || y > 1280f || !pageMotion.CanDraw(row.Key)) continue;
            float offset = pageMotion.Offset(row.Key);
            if (row.Header) RenderDeck(row, y, visual.Motion.Current.Open, offset);
            else if (row.Group != null && !visual.Retiring)
                Draw.Rect(145f + offset, y - 132f, 4f, 264f, Color.LightSkyBlue * 0.6f);
            if (movingKey == row.Key)
                Draw.Rect(154f + offset, y - Math.Min(100f, row.Height / 3f), 5f,
                    Math.Min(200f, row.Height * 2f / 3f), Color.LightGoldenrodYellow);
        }
        if (menu != null && pageMotion.Interactive) menu.Render();
        else if (pageMotion.Interactive && !Leaving && !entering)
        {
            // Bound each hint's footprint, rather than assuming native textures are 64px tall.
            Vector2 searchHint = new(100f, 920f);
            MTexture icon = GFX.Gui["menu/mapsearch"];
            float iconScale = 72f / Math.Max(1, Math.Max(icon.Width, icon.Height));
            icon.DrawCentered(searchHint, Color.White, iconScale);
            DrawHintKey(Input.GuiKey(Keys.R), searchHint);
            RenderManageHint(new Vector2(100f, 1016f));
        }
        if (toastTime > 0 && toast != null && pageMotion.Interactive && !Leaving)
        {
            Draw.Rect(180, 20, 1560, 65, Color.Black * 0.9f);
            Text(toast, new Vector2(960, 52), 0.5f, Color.LightSalmon);
        }
    }

    private static void RenderManageHint(Vector2 position)
    {
        // A small stack of outlined cards identifies deck management without another text label.
        for (int layer = 2; layer >= 0; layer--)
        {
            float x = position.X - 28f + layer * 6f;
            float y = position.Y - 20f - layer * 5f;
            Draw.Rect(x - 2f, y - 2f, 48f, 36f, Color.Black * 0.6f);
            Draw.Rect(x, y, 44f, 32f, Color.White);
            Draw.Rect(x + 3f, y + 3f, 38f, 26f, new Color(35, 39, 48));
        }
        Draw.Rect(position.X - 20f, position.Y - 11f, 20f, 3f, Color.White);
        Draw.Rect(position.X - 20f, position.Y - 3f, 14f, 3f, Color.White);
        // Default Tab; reflect any existing user rebind rather than showing a stale hint.
        MTexture key = Input.GuiKey(Input.FirstKey(CompactSaveDeckModule.Settings.ManageDecks.Button));
        DrawHintKey(key, position);
    }

    private static void DrawHintKey(MTexture key, Vector2 position)
    {
        float scale = Math.Min(1f, 48f / Math.Max(1, key.Height));
        key.Draw(position, Vector2.Zero, Color.White, scale);
    }
    private void RenderDeck(Row row, float y, float open, float offset)
    {
        Deck deck = row.Group!;
        bool highlighted = Current?.Key == row.Key;
        Color accent = highlighted ? Color.LightGoldenrodYellow : Color.LightSkyBlue;
        open = Math.Clamp(open, 0f, 1f);
        if (open > 0f)
        {
            Draw.Rect(520 + offset, y - 42, 880, 84, new Color(25, 34, 55) * (0.95f * open));
            Draw.Rect(520 + offset, y - 42, 5, 84, accent * open);
            Text("v  " + deck.Name + "  (" + deck.Slots.Count + ")", new Vector2(960 + offset, y), 0.7f, accent * open);
        }
        if (open < 1f)
        {
            float cover = 1f - open;
            MTexture card = deckCardTexture ??= MTN.FileSelect.GetAtlasSubtextures("card")[0];
            Vector2 center = new(960 + offset, y);
            card.DrawCentered(center + new Vector2(24, 18), new Color(100, 110, 125) * cover);
            card.DrawCentered(center + new Vector2(12, 9), new Color(175, 180, 190) * cover);
            card.DrawCentered(center, (highlighted ? new Color(255, 246, 220) : Color.White) * cover);
            Text(deck.Name, center + new Vector2(0, -38), 0.9f, new Color(45, 40, 50) * cover, 620f, false);
            Text(deck.Slots.Count + " " + T("saves"), center + new Vector2(0, 35), 0.58f, Color.DarkSlateGray * cover, 620f, false);
            Text(">", center + new Vector2(0, 95), 0.45f, Color.DarkSlateGray * cover, 620f, false);
        }
    }

    private static void Text(string value, Vector2 position, float scale, Color color, float width = 1400f, bool outline = true)
    {
        float actual = Math.Min(scale, width / Math.Max(1f, ActiveFont.Measure(value).X));
        if (outline) ActiveFont.DrawOutline(value, position, new Vector2(0.5f), Vector2.One * actual, color, 2f, Color.Black * (color.A / 255f));
        else ActiveFont.Draw(value, position, new Vector2(0.5f), Vector2.One * actual, color);
    }

    public void Dispose()
    {
        menu?.Dispose();
        menu = null;
        overlay?.RemoveSelf();
        overlay = null;
        Ready = false;
    }

    private sealed class DeckOverlay : Entity
    {
        private readonly DeckController controller;
        public DeckOverlay(DeckController controller)
        {
            this.controller = controller;
            Tag = Tags.HUD;
            Depth = -10000;
            Add(new BeforeRenderHook(controller.SyncSlotsForRender));
        }
        public override void Render() => controller.Render();
    }
}




