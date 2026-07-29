using System.Globalization;

namespace RetroTools.Core.Palettes;

/// <summary>
/// Χρώμα 8 bit ανά κανάλι, όπως εμφανίζεται στην οθόνη του χρήστη.
/// Τα δεδομένα των sprites ΔΕΝ αποθηκεύονται ποτέ έτσι — μόνο ως δείκτες παλέτας.
/// </summary>
public readonly record struct Rgb24(byte R, byte G, byte B)
{
    public string ToHex()
    {
        return "#" + R.ToString("X2", CultureInfo.InvariantCulture)
                   + G.ToString("X2", CultureInfo.InvariantCulture)
                   + B.ToString("X2", CultureInfo.InvariantCulture);
    }

    public static Rgb24 FromHex(string hex)
    {
        if (hex == null)
        {
            throw new ArgumentNullException(nameof(hex));
        }

        var value = hex.StartsWith("#", StringComparison.Ordinal) ? hex.Substring(1) : hex;

        if (value.Length != 6)
        {
            throw new FormatException("Αναμενόταν χρώμα σε μορφή #RRGGBB, βρέθηκε: " + hex);
        }

        return new Rgb24(
            byte.Parse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Τετράγωνο απόστασης σε γραμμικό (sRGB-linearised) χώρο, για quantization κατά το import PNG.
    /// Το γραμμικό είναι σημαντικό: στον χώρο sRGB το "μισό" 0x80 δεν είναι οπτικά μισό.
    /// </summary>
    public double LinearDistanceSquaredTo(Rgb24 other)
    {
        var dr = Linearise(R) - Linearise(other.R);
        var dg = Linearise(G) - Linearise(other.G);
        var db = Linearise(B) - Linearise(other.B);

        // Συντελεστές φωτεινότητας κατά ITU-R BT.709.
        return (0.2126 * dr * dr) + (0.7152 * dg * dg) + (0.0722 * db * db);
    }

    private static double Linearise(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    public override string ToString()
    {
        return ToHex();
    }
}
