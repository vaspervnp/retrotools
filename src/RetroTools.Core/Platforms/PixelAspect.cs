namespace RetroTools.Core.Platforms;

/// <summary>
/// Αναλογία σχήματος ενός pixel στην οθόνη. Κρίσιμο για σωστό preview:
/// ένα CPC Mode 0 pixel είναι διπλάσιου πλάτους από ό,τι ύψους, ενώ ένα
/// Mode 2 pixel είναι μισού πλάτους. Χωρίς αυτό, κάθε sprite φαίνεται λάθος.
/// </summary>
public readonly record struct PixelAspect(int Width, int Height)
{
    public static readonly PixelAspect Square = new PixelAspect(1, 1);

    /// <summary>Φαρδιά pixels (CPC Mode 0/3, C64 multicolor).</summary>
    public static readonly PixelAspect Wide = new PixelAspect(2, 1);

    /// <summary>Στενά pixels (CPC Mode 2).</summary>
    public static readonly PixelAspect Narrow = new PixelAspect(1, 2);

    public double Ratio
    {
        get { return (double)Width / Height; }
    }

    public override string ToString()
    {
        return Width + ":" + Height;
    }
}
