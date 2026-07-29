using RetroTools.Core.Platforms;

namespace RetroTools.Core.Serialization;

/// <summary>
/// Επικυρώνει ένα εισερχόμενο <see cref="ProjectDocument"/>.
/// </summary>
/// <remarks>
/// Ένα ανεβασμένο αρχείο είναι <b>μη έμπιστη είσοδος</b>. Οι έλεγχοι εδώ δεν είναι
/// ευγένεια προς τον χρήστη — είναι το όριο ανάμεσα σε «λάθος αρχείο» και σε
/// κατεστραμμένα δεδομένα ή εξαντλημένη μνήμη διακομιστή.
/// </remarks>
public static class ProjectDocumentValidator
{
    /// <summary>Ανώτατα όρια ώστε ένα κακόβουλο αρχείο να μη ρίξει τον διακομιστή.</summary>
    public const int MaxSprites = 2048;

    public const int MaxFramesPerSprite = 256;

    public const int MaxSpriteMaps = 256;

    public const int MaxSpriteDimension = 512;

    public static IReadOnlyList<string> Validate(ProjectDocument? document)
    {
        var errors = new List<string>();

        if (document == null)
        {
            errors.Add("Το αρχείο δεν περιέχει έγκυρο JSON.");
            return errors;
        }

        if (!string.Equals(document.Format, ProjectDocument.FormatIdentifier, StringComparison.Ordinal))
        {
            errors.Add("Το αρχείο δεν είναι project του RetroTools (πεδίο 'format').");
            return errors;
        }

        if (document.Version != ProjectDocument.CurrentVersion)
        {
            errors.Add(
                "Έκδοση μορφής " + document.Version + " — αυτή η εγκατάσταση διαβάζει " +
                ProjectDocument.CurrentVersion + ".");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            errors.Add("Λείπει το όνομα του project.");
        }

        if (!PlatformCatalog.TryGetMode(document.ModeCode, out var mode) || mode == null)
        {
            errors.Add("Άγνωστο mode '" + document.ModeCode + "'.");
            return errors;
        }

        // Η πλατφόρμα προκύπτει από το mode· αν το αρχείο δηλώνει άλλη, κάτι δεν πάει καλά.
        var platform = PlatformCatalog.Get(mode.Platform);

        if (!string.IsNullOrWhiteSpace(document.PlatformCode)
            && !string.Equals(document.PlatformCode, platform.Code, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                "Ασυνέπεια: το mode '" + mode.Code + "' ανήκει στο '" + platform.Code +
                "' αλλά το αρχείο δηλώνει '" + document.PlatformCode + "'.");
        }

        ValidatePalette(document, mode, platform, errors);
        ValidateGroups(document, errors);
        ValidateSprites(document, mode, errors);
        ValidateSpriteMaps(document, errors);

        return errors;
    }

    private static void ValidatePalette(
        ProjectDocument document,
        GraphicsMode mode,
        PlatformDefinition platform,
        List<string> errors)
    {
        var seen = new HashSet<int>();

        foreach (var entry in document.Palette)
        {
            if (entry.Slot < 0 || entry.Slot > mode.MaxPixelValue)
            {
                errors.Add("Slot παλέτας " + entry.Slot + " εκτός ορίων 0–" + mode.MaxPixelValue + ".");
                continue;
            }

            if (!seen.Add(entry.Slot))
            {
                errors.Add("Το slot παλέτας " + entry.Slot + " ορίζεται δύο φορές.");
            }

            if (entry.Color < 0 || entry.Color >= platform.Palette.Count)
            {
                errors.Add(
                    "Χρώμα " + entry.Color + " εκτός της παλέτας του " + platform.Name +
                    " (0–" + (platform.Palette.Count - 1) + ").");
            }
        }
    }

    private static void ValidateGroups(ProjectDocument document, List<string> errors)
    {
        var seen = new HashSet<int>();

        foreach (var group in document.Groups)
        {
            if (!seen.Add(group.Id))
            {
                errors.Add("Διπλό αναγνωριστικό ομάδας: " + group.Id + ".");
            }

            if (string.IsNullOrWhiteSpace(group.Name))
            {
                errors.Add("Ομάδα χωρίς όνομα (id " + group.Id + ").");
            }
        }
    }

    private static void ValidateSprites(ProjectDocument document, GraphicsMode mode, List<string> errors)
    {
        if (document.Sprites.Count > MaxSprites)
        {
            errors.Add("Πάρα πολλά sprites (" + document.Sprites.Count + "· ανώτατο " + MaxSprites + ").");
            return;
        }

        var groupIds = document.Groups.Select(g => g.Id).ToHashSet();
        var spriteIds = new HashSet<int>();

        foreach (var sprite in document.Sprites)
        {
            var label = "Sprite '" + sprite.Name + "' (id " + sprite.Id + "): ";

            if (!spriteIds.Add(sprite.Id))
            {
                errors.Add("Διπλό αναγνωριστικό sprite: " + sprite.Id + ".");
            }

            if (string.IsNullOrWhiteSpace(sprite.Name))
            {
                errors.Add("Sprite χωρίς όνομα (id " + sprite.Id + ").");
            }

            if (sprite.GroupId.HasValue && !groupIds.Contains(sprite.GroupId.Value))
            {
                errors.Add(label + "αναφέρεται σε ανύπαρκτη ομάδα " + sprite.GroupId.Value + ".");
            }

            if (sprite.Width <= 0 || sprite.Height <= 0
                || sprite.Width > MaxSpriteDimension || sprite.Height > MaxSpriteDimension)
            {
                errors.Add(label + "μη έγκυρες διαστάσεις " + sprite.Width + "×" + sprite.Height + ".");
                continue;
            }

            foreach (var sizeError in mode.SpriteSize.Validate(sprite.Width, sprite.Height))
            {
                errors.Add(label + sizeError);
            }

            ValidateFrames(sprite, mode, label, errors);
        }
    }

    private static void ValidateFrames(
        SpriteDocument sprite,
        GraphicsMode mode,
        string label,
        List<string> errors)
    {
        if (sprite.Frames.Count == 0)
        {
            errors.Add(label + "δεν έχει κανένα καρέ.");
            return;
        }

        if (sprite.Frames.Count > MaxFramesPerSprite)
        {
            errors.Add(label + "πάρα πολλά καρέ (ανώτατο " + MaxFramesPerSprite + ").");
            return;
        }

        var expectedPixels = sprite.Width * sprite.Height;
        var expectedAttributes = ExpectedAttributeCount(sprite, mode);
        var frameIndexes = new HashSet<int>();

        foreach (var frame in sprite.Frames)
        {
            var frameLabel = label + "καρέ " + frame.Index + ": ";

            if (!frameIndexes.Add(frame.Index))
            {
                errors.Add(label + "διπλό καρέ με δείκτη " + frame.Index + ".");
            }

            if (!TryDecode(frame.Pixels, out var pixels))
            {
                errors.Add(frameLabel + "τα pixels δεν είναι έγκυρο base64.");
                continue;
            }

            if (pixels.Length != expectedPixels)
            {
                errors.Add(
                    frameLabel + "αναμένονταν " + expectedPixels + " bytes pixels, βρέθηκαν " + pixels.Length + ".");
                continue;
            }

            // Χωρίς αυτόν τον έλεγχο μια τιμή εκτός ορίων θα αποθηκευόταν και θα
            // «ξεχείλιζε» σε γειτονικά pixels στο export — μακριά από την αιτία.
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i] > mode.MaxPixelValue)
                {
                    errors.Add(
                        frameLabel + "χρώμα " + pixels[i] + " εκτός ορίων 0–" + mode.MaxPixelValue +
                        " στη θέση " + (i % sprite.Width) + "," + (i / sprite.Width) + ".");
                    break;
                }
            }

            if (!string.IsNullOrEmpty(frame.Attributes))
            {
                if (!TryDecode(frame.Attributes, out var attributes))
                {
                    errors.Add(frameLabel + "τα attributes δεν είναι έγκυρο base64.");
                }
                else if (expectedAttributes == 0)
                {
                    errors.Add(frameLabel + "το mode " + mode.Name + " δεν χρησιμοποιεί attributes.");
                }
                else if (attributes.Length != expectedAttributes)
                {
                    errors.Add(
                        frameLabel + "αναμένονταν " + expectedAttributes + " attributes, βρέθηκαν " +
                        attributes.Length + ".");
                }
            }

            if (!string.IsNullOrEmpty(frame.Mask))
            {
                if (!TryDecode(frame.Mask, out var mask))
                {
                    errors.Add(frameLabel + "η μάσκα δεν είναι έγκυρο base64.");
                }
                else if (mask.Length != expectedPixels)
                {
                    errors.Add(
                        frameLabel + "η μάσκα πρέπει να έχει " + expectedPixels + " bytes, έχει " + mask.Length + ".");
                }
            }
        }
    }

    private static void ValidateSpriteMaps(ProjectDocument document, List<string> errors)
    {
        if (document.SpriteMaps.Count > MaxSpriteMaps)
        {
            errors.Add("Πάρα πολλά spritemaps (ανώτατο " + MaxSpriteMaps + ").");
            return;
        }

        var spriteIds = document.Sprites.Select(s => s.Id).ToHashSet();

        foreach (var map in document.SpriteMaps)
        {
            var label = "Spritemap '" + map.Name + "': ";

            if (string.IsNullOrWhiteSpace(map.Name))
            {
                errors.Add("Spritemap χωρίς όνομα.");
            }

            if (map.Columns < 1 || map.Columns > 64 || map.Rows < 1 || map.Rows > 64)
            {
                errors.Add(label + "μη έγκυρο πλέγμα " + map.Columns + "×" + map.Rows + " (1–64).");
                continue;
            }

            var positions = new HashSet<(int, int)>();

            foreach (var cell in map.Cells)
            {
                if (cell.Column < 0 || cell.Column >= map.Columns || cell.Row < 0 || cell.Row >= map.Rows)
                {
                    errors.Add(label + "κελί " + cell.Column + "," + cell.Row + " εκτός πλέγματος.");
                    continue;
                }

                if (!positions.Add((cell.Column, cell.Row)))
                {
                    errors.Add(label + "δύο κελιά στη θέση " + cell.Column + "," + cell.Row + ".");
                }

                if (!spriteIds.Contains(cell.SpriteId))
                {
                    errors.Add(label + "κελί δείχνει σε ανύπαρκτο sprite " + cell.SpriteId + ".");
                }
            }
        }
    }

    private static int ExpectedAttributeCount(SpriteDocument sprite, GraphicsMode mode)
    {
        if (mode.ColorScope != ColorScope.PerCell || mode.CellWidth == 0 || mode.CellHeight == 0)
        {
            return 0;
        }

        var columns = (sprite.Width + mode.CellWidth - 1) / mode.CellWidth;
        var rows = (sprite.Height + mode.CellHeight - 1) / mode.CellHeight;

        return columns * rows;
    }

    private static bool TryDecode(string? value, out byte[] data)
    {
        data = Array.Empty<byte>();

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            data = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
