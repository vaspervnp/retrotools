namespace RetroTools.Core.Platforms;

/// <summary>
/// Από πού παίρνει το χρώμα του ένα pixel. Δεν είναι λεπτομέρεια:
/// στον C64 τα multicolor slots 1 και 3 είναι <b>κοινοί καταχωρητές για όλα τα sprites</b>,
/// οπότε αλλάζοντάς τα σε ένα sprite αλλάζουν όλα. Ο editor πρέπει να το λέει.
/// </summary>
public enum PixelSlotRole
{
    /// <summary>Ελεύθερα επιλέξιμο pen της παλέτας του project (CPC).</summary>
    Free = 0,

    /// <summary>Δεν σχεδιάζεται — φαίνεται ό,τι υπάρχει από κάτω.</summary>
    Transparent = 1,

    /// <summary>Καταχωρητής κοινός για όλα τα αντικείμενα της οθόνης. Αλλαγή = καθολική αλλαγή.</summary>
    Shared = 2,

    /// <summary>Χρώμα ανά αντικείμενο: ανά sprite ή ανά κελί 8×8.</summary>
    PerObject = 3,
}

/// <summary>
/// Ένα slot της παλέτας ενός mode, δηλαδή μία δυνατή τιμή pixel.
/// </summary>
/// <param name="Index">Η τιμή του pixel στο indexed buffer.</param>
/// <param name="Name">Τι είναι, στη γλώσσα της πλατφόρμας ("INK", "Sprite Multicolor 0").</param>
/// <param name="HardwareRegister">
/// Ο καταχωρητής ή η πηγή του χρώματος στο υλικό ("$D025", "Colour RAM", "attribute INK").
/// Κενό όταν πρόκειται για ελεύθερο pen.
/// </param>
public sealed record PixelSlot(int Index, string Name, PixelSlotRole Role, string HardwareRegister)
{
    public static PixelSlot Free(int index)
    {
        return new PixelSlot(index, "Pen " + index, PixelSlotRole.Free, string.Empty);
    }

    /// <summary>Είναι αλλαγή που επηρεάζει και άλλα sprites/κελιά;</summary>
    public bool IsGlobal
    {
        get { return Role == PixelSlotRole.Shared; }
    }
}
