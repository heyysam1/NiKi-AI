namespace NikiAI.Core.Character;

/// <summary>
/// Represents an animation cycle for a character state, composed of one or more sprite frames.
/// </summary>
public class CharacterAnimation
{
    public CharacterState State { get; init; }
    public IReadOnlyList<SpriteFrame> Frames { get; init; }
    public bool IsLooping { get; init; } = true;

    public int TotalDurationMs => Frames.Sum(f => f.DurationMs);

    public CharacterAnimation(CharacterState state, IEnumerable<SpriteFrame> frames, bool isLooping = true)
    {
        State = state;
        Frames = frames.ToList().AsReadOnly();
        IsLooping = isLooping;

        if (Frames.Count == 0)
        {
            throw new ArgumentException("Animation must contain at least one frame.", nameof(frames));
        }
    }

    public SpriteFrame GetFrame(int index)
    {
        if (Frames.Count == 0)
        {
            throw new InvalidOperationException("Animation has no frames.");
        }

        if (IsLooping)
        {
            var normalizedIndex = ((index % Frames.Count) + Frames.Count) % Frames.Count;
            return Frames[normalizedIndex];
        }

        var clampedIndex = Math.Clamp(index, 0, Frames.Count - 1);
        return Frames[clampedIndex];
    }

    public SpriteFrame GetFrameAtTime(int elapsedMs)
    {
        if (Frames.Count == 0)
        {
            throw new InvalidOperationException("Animation has no frames.");
        }

        if (Frames.Count == 1 || TotalDurationMs <= 0)
        {
            return Frames[0];
        }

        var time = IsLooping
            ? ((elapsedMs % TotalDurationMs) + TotalDurationMs) % TotalDurationMs
            : Math.Clamp(elapsedMs, 0, TotalDurationMs - 1);

        var accumulated = 0;
        foreach (var frame in Frames)
        {
            accumulated += frame.DurationMs;
            if (time < accumulated)
            {
                return frame;
            }
        }

        return Frames[^1];
    }
}
