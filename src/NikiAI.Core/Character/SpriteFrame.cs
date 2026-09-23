namespace NikiAI.Core.Character;

/// <summary>
/// Represents a single frame within a character's pixel-art animation sequence.
/// </summary>
public record SpriteFrame
{
    public string ImagePath { get; init; } = string.Empty;
    public string AssetPath => ImagePath;
    public int DurationMs { get; init; } = 250;
    public int Width { get; init; } = 48;
    public int Height { get; init; } = 48;

    public SpriteFrame() { }

    public SpriteFrame(string imagePath, int durationMs = 250, int width = 48, int height = 48)
    {
        ImagePath = imagePath;
        DurationMs = durationMs > 0 ? durationMs : 250;
        Width = width;
        Height = height;
    }
}
