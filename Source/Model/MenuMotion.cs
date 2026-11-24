namespace Celeste.Mod.CompactSaveDeck.Model;

// Everest Mod Options uses a quarter-second CubeOut entrance and CubeIn exit.
public sealed class MenuMotion
{
    public const float Duration = 0.25f;
    private float elapsed;
    private float exitFrom;
    public float Amount { get; private set; }
    public bool Closing { get; private set; }
    public bool Closed => Closing && elapsed >= Duration;
    public bool Interactive => !Closing && elapsed >= Duration;
    public float Offset => 1920f * (1f - Amount);

    public void Close()
    {
        if (Closing) return;
        exitFrom = Amount;
        elapsed = 0;
        Closing = true;
    }

    public void Advance(float dt)
    {
        elapsed = Math.Min(Duration, elapsed + Math.Max(0, dt));
        float t = elapsed / Duration;
        Amount = Closing ? exitFrom * (1f - t * t * t) : 1f - MathF.Pow(1f - t, 3);
    }
}
