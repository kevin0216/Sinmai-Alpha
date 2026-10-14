namespace SinmaiAlpha.Compatibility;

internal static class PracticeMode
{
    // MaiMagic/AquaMai PracticeMode.speed, read live through a bound field ref; 1 when absent.
    internal static float speed => OptionalDisplay.PracticeSpeed;
}
