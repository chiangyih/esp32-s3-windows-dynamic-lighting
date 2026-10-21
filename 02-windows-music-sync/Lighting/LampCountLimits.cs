namespace MusicSync.Lighting;

public static class LampCountLimits
{
    public const int Minimum = 1;
    public const int Maximum = 64;
    public const int Default = 8;

    public static int Validate(int count)
    {
        if (count < Minimum || count > Maximum)
            throw new ArgumentOutOfRangeException(nameof(count), count, "燈數必須介於 1～64 顆。");
        return count;
    }
}
