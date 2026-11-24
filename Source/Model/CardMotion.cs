namespace Celeste.Mod.CompactSaveDeck.Model;

public readonly record struct CardPose(float X, float Y, float Open = 0)
{
    public static CardPose Lerp(CardPose from, CardPose to, float t) =>
        new(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t, from.Open + (to.Open - from.Open) * t);
}

// Match vanilla save-slot MoveTo / ScrollTo timing and easing.
public sealed class CardMotion
{
    public const float Duration = 0.25f;
    public CardPose Current { get; private set; }
    public CardPose Target { get; private set; }
    public bool Complete { get; private set; } = true;
    private CardPose from;
    private float elapsed, delay;
    private bool interrupted;

    public CardMotion(CardPose initial) => Current = Target = from = initial;

    public void Retarget(CardPose target, float stagger = 0, bool immediate = false)
    {
        if (immediate) { Current = Target = from = target; Complete = true; return; }
        if (target == Target) return;
        interrupted = !Complete;
        from = Current;
        Target = target;
        elapsed = 0;
        delay = interrupted ? 0 : Math.Clamp(stagger, 0, 0.06f);
        Complete = from == target;
    }

    public void Advance(float deltaTime)
    {
        if (Complete) return;
        elapsed += Math.Max(0, deltaTime);
        float t = Math.Clamp((elapsed - delay) / Duration, 0, 1);
        float eased = interrupted ? 1 - MathF.Pow(1 - t, 3) :
            t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
        Current = CardPose.Lerp(from, Target, eased);
        if (t >= 1) { Current = Target; Complete = true; }
    }
}
