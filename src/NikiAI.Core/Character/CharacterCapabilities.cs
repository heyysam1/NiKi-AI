namespace NikiAI.Core.Character;

/// <summary>
/// Anatomical and component-level capabilities of a Desktop Pet character.
/// Used to validate motion primitives and prevent impossible or identity-violating actions.
/// </summary>
[Flags]
public enum CharacterCapability : long
{
    None = 0,
    HeadTilt = 1L << 0,
    LookAround = 1L << 1,
    Blink = 1L << 2,
    Nod = 1L << 3,
    Smile = 1L << 4,
    Hands = 1L << 5,
    Arms = 1L << 6,
    Torso = 1L << 7,
    Legs = 1L << 8,
    Tail = 1L << 9,
    FloppyEars = 1L << 10,
    CatEars = 1L << 11,
    Forepaws = 1L << 12,
    Visor = 1L << 13,
    Armor = 1L << 14,
    Plume = 1L << 15,
    Sword = 1L << 16,
    Helmet = 1L << 17,
    Antenna = 1L << 18,
    HairGroups = 1L << 19,
    Jacket = 1L << 20,
    Cap = 1L << 21,
    Overalls = 1L << 22,
    WeightShift = 1L << 23,
    Bounce = 1L << 24,
    Stretch = 1L << 25,
    GuardStance = 1L << 26,
    FistPump = 1L << 27
}
