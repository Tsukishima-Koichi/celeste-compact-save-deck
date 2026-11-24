namespace Celeste.Mod.CompactSaveDeck.Model;

// Shared by native save cards and deck covers. Entry is armed during loading,
// but starts only when vanilla calls Show after its main-menu hand-off.
public sealed class SavePageMotion
{
    private enum Phase { Hidden, Waiting, Entering, Active, Leaving }
    private Phase phase;
    private readonly Dictionary<string, CardMotion> tracks = new();
    public bool Drawing => phase is Phase.Entering or Phase.Active or Phase.Leaving;
    public bool Interactive => phase == Phase.Active;
    public bool Exiting => phase == Phase.Leaving;

    public void ArmEntry()
    {
        tracks.Clear();
        phase = Phase.Waiting;
    }

    public void Show(IEnumerable<string> visibleRows)
    {
        if (phase != Phase.Waiting) return;
        int index = 0;
        foreach (string key in visibleRows.Distinct())
        {
            var track = new CardMotion(new CardPose(1920f, 0));
            track.Retarget(new CardPose(0, 0), index++ * 0.02f);
            tracks.Add(key, track);
        }
        phase = tracks.Count == 0 ? Phase.Active : Phase.Entering;
    }

    // Native naming / assist submenus return to the selected card, not a list entrance.
    public void Settle()
    {
        tracks.Clear();
        phase = Phase.Active;
    }

    public void Hide(IEnumerable<string> visibleRows, int direction)
    {
        if (!Drawing) { tracks.Clear(); phase = Phase.Hidden; return; }
        int index = 0;
        var retained = new HashSet<string>();
        foreach (string key in visibleRows.Distinct())
        {
            retained.Add(key);
            if (!tracks.TryGetValue(key, out var track))
                tracks.Add(key, track = new CardMotion(new CardPose(0, 0)));
            track.Retarget(new CardPose(direction < 0 ? -1920f : 1920f, 0), index++ * 0.02f);
        }
        foreach (string key in tracks.Keys.Where(k => !retained.Contains(k)).ToArray()) tracks.Remove(key);
        phase = tracks.Count == 0 ? Phase.Hidden : Phase.Leaving;
    }

    public bool CanDraw(string key) => phase == Phase.Active ||
        ((phase is Phase.Entering or Phase.Leaving) && tracks.ContainsKey(key));

    public float Offset(string key) => tracks.TryGetValue(key, out var track) ? track.Current.X : 0f;

    public void Advance(float deltaTime)
    {
        if (phase is not (Phase.Entering or Phase.Leaving)) return;
        bool complete = true;
        foreach (CardMotion track in tracks.Values)
        {
            track.Advance(deltaTime);
            complete &= track.Complete;
        }
        if (!complete) return;
        if (phase == Phase.Entering) { tracks.Clear(); phase = Phase.Active; }
        else { phase = Phase.Hidden; }
    }
}

