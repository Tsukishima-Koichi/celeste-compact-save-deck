using Celeste.Mod.CompactSaveDeck.Model;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.CompactSaveDeck;

// Native TextMenu owns fonts, highlight accessibility settings, selection wiggles and sounds.
// The panel owns a clipped vertical viewport and its lifetime, including outgoing frames.
internal sealed class DeckMenuPanel : IDisposable
{
    private readonly TextMenu view;
    private readonly FittedHeader header;
    private readonly List<(Func<string> Label, Action Action)> entries;
    private readonly List<TextMenu.Button> buttons = new();
    private readonly MenuMotion motion = new();
    private readonly Action back;
    private readonly TextMenuExt.TextBox? searchBox;
    private readonly Action<string, DeckMenuPanel>? search;
    private bool beginTyping;
    private readonly bool searchOnDemand;
    private bool searchVisible;
    private float searchAmount;
    private int inputCooldown;
    public bool ShowSearchHint => searchOnDemand && motion.Interactive && !searchVisible;

    public void FinishSearch()
    {
        searchBox?.StopTyping();
        searchVisible = false;
        beginTyping = false;
        inputCooldown = 2;
        Input.MenuConfirm.ConsumePress();
    }
    private float scroll;
    private bool disposed;
    public bool Closed => motion.Closed;
    public Action? AfterClose { get; private set; }
    public string? Feedback { get; set; }
    private const float ViewWidth = 1320f;
    private const float Top = 270f;
    private const float Bottom = 915f;

    public DeckMenuPanel(Scene scene, Overworld overworld, string title,
        IEnumerable<(Func<string> Label, Action Action)> entries, Action back,
        Action<string, DeckMenuPanel>? search = null, bool searchOnDemand = false)
    {
        this.entries = entries.ToList();
        this.back = back;
        this.search = search;
        this.searchOnDemand = searchOnDemand;
        view = new TextMenu {
            Active = false, Visible = false, Focused = false, AutoScroll = false,
            RenderAsFocused = true, MinWidth = ViewWidth,
            InnerContent = search == null || searchOnDemand ? TextMenu.InnerContentMode.OneColumn : TextMenu.InnerContentMode.TwoColumn, BatchMode = true
        };
        header = new FittedHeader(title) { Container = view };
        foreach (var entry in this.entries)
        {
            var button = new TextMenu.Button(Fit(entry.Label()));
            rawLabels[buttons.Count] = entry.Label();
            buttons.Add(button);
            view.Add(button);
        }
        view.BatchMode = false;
        scene.Add(view);
        if (search != null)
        {
            searchBox = new TextMenuExt.TextBox(overworld) {
                Container = view, PlaceholderText = Dialog.Clean(searchOnDemand ? "csd_search_decks_placeholder" : "csd_search_placeholder")
            };
            searchBox.OnTextChange += _ => Feedback = null;
            searchBox.OnTextInputCharActions['\r'] = _ => SubmitSearch();
            searchBox.OnTextInputCharActions['\n'] = _ => { };
            searchBox.AfterInputConsumed = HandleSearchNavigation;
            beginTyping = !searchOnDemand;
            searchVisible = !searchOnDemand;
        }
        Audio.Play("event:/ui/main/whoosh_large_in");
    }

    private sealed class FittedHeader(string title) : TextMenu.Header(title)
    {
        public override void Render(Vector2 position, bool highlighted)
        {
            float scale = Math.Min(2f, ViewWidth / Math.Max(1f, ActiveFont.Measure(Title).X));
            if (scale == 2f) { base.Render(position, highlighted); return; }
            float alpha = Container.Alpha;
            ActiveFont.DrawEdgeOutline(Title, position + new Vector2(ViewWidth * 0.5f, 0),
                new Vector2(0.5f), Vector2.One * scale, Color.Gray * alpha,
                2f * scale, Color.DarkSlateBlue * alpha, 2f, Color.Black * (alpha * alpha * alpha));
        }
    }

    private static string Fit(string label)
    {
        if (ActiveFont.Measure(label).X <= ViewWidth) return label;
        int length = label.Length;
        while (length > 0 && ActiveFont.Measure(label[..length] + "...").X > ViewWidth) length--;
        if (length > 0 && char.IsHighSurrogate(label[length - 1])) length--;
        return label[..length] + "...";
    }

    public void Close(Action? next = null)
    {
        if (motion.Closing) return;
        AfterClose = next;
        beginTyping = false;
        searchBox?.StopTyping();
        view.Focused = false;
        view.RenderAsFocused = true;
        motion.Close();
        Audio.Play("event:/ui/main/whoosh_large_out");
    }

    public void Update()
    {
        motion.Advance(Engine.RawDeltaTime);
        if (inputCooldown > 0) inputCooldown--;
        searchAmount = Calc.Approach(searchAmount, searchVisible ? 1f : 0f, Engine.RawDeltaTime / 0.25f);
        view.Focused = false;
        view.RenderAsFocused = true;
        for (int i = 0; i < buttons.Count; i++)
        {
            // Labels can change in-place (e.g. the selected member count).
            string label = entries[i].Label();
            if (label != rawLabels.GetValueOrDefault(i))
            {
                rawLabels[i] = label;
                buttons[i].Label = Fit(label);
            }
        }
        view.Update();
        if (beginTyping && motion.Interactive)
        {
            beginTyping = false;
            searchBox!.StartTyping();
        }
        if (searchBox?.Typing == true) searchBox.Update();

        float step = ActiveFont.LineHeight + view.ItemSpacing;
        float available = Bottom - Top;
        float target = buttons.Count == 0 ? 0 :
            Math.Clamp(view.Selection * step - available * 0.5f, 0,
                Math.Max(0, buttons.Count * step - available));
        scroll = Calc.Approach(scroll, target,
            Math.Max(1600f, Math.Abs(target - scroll) * 14f) * Engine.RawDeltaTime);
    }

    private readonly Dictionary<int, string> rawLabels = new();

    public void HandleInput()
    {
        if (!motion.Interactive || inputCooldown > 0) return;
        if (searchBox?.Typing == true)
        {
            // Keyboard text is processed by Everest's TextBox. Gamepad confirm also searches.
            if (Input.MenuConfirm.Pressed) { SubmitSearch(); Input.MenuConfirm.ConsumePress(); }
            return;
        }
        if (searchOnDemand && CompactSaveDeckModule.Settings.SearchDecks.Pressed)
        {
            CompactSaveDeckModule.Settings.SearchDecks.ConsumePress();
            searchVisible = true;
            beginTyping = true;
            Feedback = null;
            return;
        }
        if (Input.MenuCancel.Pressed || Input.ESC.Pressed)
        {
            Input.MenuCancel.ConsumePress();
            Input.ESC.ConsumePress();
            Audio.Play("event:/ui/main/button_back");
            if (searchOnDemand && searchVisible) FinishSearch();
            else back();
        }
        else if (buttons.Count > 0)
        {
            if (Input.MenuUp.Pressed) view.MoveSelection(-1, true);
            else if (Input.MenuDown.Pressed) view.MoveSelection(1, true);
            else if (Input.MenuConfirm.Pressed)
            {
                Input.MenuConfirm.ConsumePress();
                buttons[view.Selection].ConfirmPressed();
                entries[view.Selection].Action();
            }
        }
    }

    private void HandleSearchNavigation()
    {
        if (!motion.Interactive || inputCooldown > 0) return;
        if (Input.ESC.Pressed || Input.MenuCancel.Pressed)
        {
            Input.MenuCancel.ConsumePress();
            Input.ESC.ConsumePress();
            Audio.Play("event:/ui/main/button_back");
            if (searchOnDemand && searchVisible) FinishSearch();
            else back();
        }
        else if (buttons.Count > 0)
        {
            if (Input.MenuUp.Pressed) view.MoveSelection(-1, true);
            else if (Input.MenuDown.Pressed) view.MoveSelection(1, true);
        }
    }

    private void SubmitSearch()
    {
        if (motion.Interactive && searchBox != null) search?.Invoke(searchBox.Text, this);
    }

    public void Highlight(int index)
    {
        if (index < 0 || index >= buttons.Count) return;
        view.Selection = index;
        buttons[index].SelectWiggler.Start();
        Feedback = null;
        Audio.Play("event:/ui/main/rollover_down");
    }

    public void Render()
    {
        float alpha = motion.Amount;
        if (alpha <= 0) return;
        view.Alpha = alpha;
        float x = (1920f - ViewWidth) * 0.5f + motion.Offset;
        Draw.Rect(0, 0, 1920, 1080, Color.Black * (0.8f * alpha));
        float step = ActiveFont.LineHeight + view.ItemSpacing;
        for (int i = 0; i < buttons.Count; i++)
        {
            float y = Top + step * (i + 0.5f) - scroll;
            if (y - step * 0.5f < Top || y + step * 0.5f > Bottom) continue;
            buttons[i].Render(new Vector2(x, y + buttons[i].SelectWiggler.Value * 8f), view.Selection == i);
        }
        if (searchBox == null || (searchOnDemand && searchAmount <= 0))
            header.Render(new Vector2(x, 145), false);
        else
        {
            // Same box and icon as Everest; the input drops in from the top.
            Vector2 boxPosition = new((1920f - ViewWidth) * 0.5f, 85f - 220f * (1f - alpha * (searchOnDemand ? Ease.CubeOut(searchAmount) : 1f)));
            searchBox.Alpha = alpha;
            searchBox.TextBoxColor = Color.DarkSlateGray * (0.8f * alpha);
            Draw.HollowRect(boxPosition.X - 2, boxPosition.Y - 2, ViewWidth + 4, searchBox.Height() + 4, Color.White * alpha);
            searchBox.Render(boxPosition, true);
        }
        if (!string.IsNullOrEmpty(Feedback))
            ActiveFont.DrawOutline(Feedback, new Vector2(960 + motion.Offset, 965), new Vector2(0.5f),
                Vector2.One * 0.6f, Color.LightGray * alpha, 2, Color.Black * alpha);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        searchBox?.StopTyping();
        view.RemoveSelf();
    }
}
