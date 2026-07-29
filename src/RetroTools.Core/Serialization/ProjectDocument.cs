namespace RetroTools.Core.Serialization;

/// <summary>
/// Πλήρες project σε μεταφέρσιμη μορφή: αντίγραφο ασφαλείας, μεταφορά ανάμεσα σε
/// εγκαταστάσεις, ή απλώς κάτι που μπορεί να μπει σε git δίπλα στον κώδικα του παιχνιδιού.
/// </summary>
/// <remarks>
/// <para>
/// Τα pixels αποθηκεύονται ως base64 του <b>ωμού indexed buffer</b> (1 byte ανά pixel)
/// και όχι ως <see cref="RsprContainer"/>: το αρχείο είναι δημόσια μορφή ανταλλαγής και
/// δεν πρέπει να δεσμεύεται από την εσωτερική μας κωδικοποίηση αποθήκευσης.
/// </para>
/// <para>
/// Τα <c>Id</c> είναι <b>τοπικά του εγγράφου</b>, όχι κλειδιά βάσης. Στην εισαγωγή
/// αντιστοιχίζονται σε νέα, ώστε ένα αρχείο να μην μπορεί να δείξει σε δεδομένα
/// άλλου χρήστη.
/// </para>
/// </remarks>
public sealed class ProjectDocument
{
    /// <summary>Σταθερό αναγνωριστικό μορφής — αποτρέπει την εισαγωγή άσχετου JSON.</summary>
    public const string FormatIdentifier = "retrotools-project";

    public const int CurrentVersion = 1;

    /// <summary>
    /// Χωρίς προεπιλογή <b>επίτηδες</b>: αν το πεδίο έμπαινε από initializer, ένα
    /// οποιοδήποτε JSON χωρίς <c>format</c> θα περνούσε τον έλεγχο ταυτότητας μορφής,
    /// αφού η αποσειριοποίηση δεν αγγίζει ιδιότητες που λείπουν από το αρχείο.
    /// Το πεδίο συμπληρώνεται από τον <see cref="ProjectDocumentSerializer"/> στην εγγραφή.
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Αυξάνεται μόνο σε ασύμβατη αλλαγή. Ο reader απορρίπτει άγνωστες εκδόσεις
    /// αντί να μαντέψει — μια σιωπηλά μισοδιαβασμένη δουλειά είναι χειρότερη από σφάλμα.
    /// Μηδέν σημαίνει «δεν δηλώθηκε», που είναι εξίσου άκυρο.
    /// </summary>
    public int Version { get; set; }

    public string? Generator { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string PlatformCode { get; set; } = string.Empty;

    public string ModeCode { get; set; } = string.Empty;

    public string? PaletteProfileId { get; set; }

    public List<PaletteSlotDocument> Palette { get; set; } = new List<PaletteSlotDocument>();

    public List<SpriteGroupDocument> Groups { get; set; } = new List<SpriteGroupDocument>();

    public List<SpriteDocument> Sprites { get; set; } = new List<SpriteDocument>();

    public List<SpriteMapDocument> SpriteMaps { get; set; } = new List<SpriteMapDocument>();
}

public sealed class PaletteSlotDocument
{
    public int Slot { get; set; }

    /// <summary>Δείκτης στην παλέτα υλικού: 0–26 (CPC), 0–15 (C64/ZX).</summary>
    public int Color { get; set; }
}

public sealed class SpriteGroupDocument
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

public sealed class SpriteDocument
{
    public int Id { get; set; }

    public int? GroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public bool HasMask { get; set; }

    public string? Meta { get; set; }

    public int SortOrder { get; set; }

    public List<SpriteFrameDocument> Frames { get; set; } = new List<SpriteFrameDocument>();
}

public sealed class SpriteFrameDocument
{
    public int Index { get; set; }

    public int DurationMs { get; set; } = 100;

    /// <summary>Base64 του indexed buffer, μήκους ακριβώς width × height.</summary>
    public string Pixels { get; set; } = string.Empty;

    /// <summary>Base64 των ZX attributes, ένα byte ανά κελί.</summary>
    public string? Attributes { get; set; }

    /// <summary>Base64 της μάσκας, 1 byte ανά pixel (1 = αδιαφανές).</summary>
    public string? Mask { get; set; }
}

public sealed class SpriteMapDocument
{
    public string Name { get; set; } = string.Empty;

    public int Columns { get; set; }

    public int Rows { get; set; }

    public int CellWidth { get; set; }

    public int CellHeight { get; set; }

    public List<SpriteMapCellDocument> Cells { get; set; } = new List<SpriteMapCellDocument>();
}

public sealed class SpriteMapCellDocument
{
    public int Column { get; set; }

    public int Row { get; set; }

    /// <summary>Αναφορά στο τοπικό <c>Id</c> ενός sprite του ίδιου εγγράφου.</summary>
    public int SpriteId { get; set; }

    public int FrameIndex { get; set; }

    public bool FlipHorizontal { get; set; }

    public bool FlipVertical { get; set; }
}
