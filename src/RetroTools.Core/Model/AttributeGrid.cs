using RetroTools.Core.Platforms.Definitions;

namespace RetroTools.Core.Model;

/// <summary>
/// Το πλέγμα attributes ενός ZX Spectrum sprite: ένα byte ανά κελί 8×8, με
/// INK, PAPER, BRIGHT και FLASH. Είναι ξεχωριστό από το bitmap γιατί ακριβώς
/// έτσι το χωρίζει και το υλικό.
/// </summary>
public sealed class AttributeGrid
{
    private readonly byte[] _attributes;

    public AttributeGrid(int columns, int rows)
    {
        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), columns, "Οι στήλες πρέπει να είναι θετικές.");
        }

        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), rows, "Οι γραμμές πρέπει να είναι θετικές.");
        }

        Columns = columns;
        Rows = rows;
        _attributes = new byte[columns * rows];

        // Προεπιλογή: μαύρο PAPER, λευκό INK — ό,τι βλέπει ο χρήστης σε άδειο sprite.
        var defaultAttribute = ZxSpectrumPlatform.MakeAttribute(ink: 7, paper: 0, bright: false, flash: false);
        Array.Fill(_attributes, defaultAttribute);
    }

    public int Columns { get; }

    public int Rows { get; }

    public byte this[int column, int row]
    {
        get
        {
            EnsureInBounds(column, row);
            return _attributes[(row * Columns) + column];
        }

        set
        {
            EnsureInBounds(column, row);
            _attributes[(row * Columns) + column] = value;
        }
    }

    /// <summary>Δημιουργεί πλέγμα στο σωστό μέγεθος για sprite δεδομένων διαστάσεων.</summary>
    public static AttributeGrid ForSprite(int widthPixels, int heightPixels, int cellWidth = 8, int cellHeight = 8)
    {
        var columns = (widthPixels + cellWidth - 1) / cellWidth;
        var rows = (heightPixels + cellHeight - 1) / cellHeight;

        return new AttributeGrid(columns, rows);
    }

    public void SetCell(int column, int row, int ink, int paper, bool bright, bool flash)
    {
        this[column, row] = ZxSpectrumPlatform.MakeAttribute(ink, paper, bright, flash);
    }

    public (int Ink, int Paper, bool Bright, bool Flash) ReadCell(int column, int row)
    {
        return ZxSpectrumPlatform.ReadAttribute(this[column, row]);
    }

    public byte[] ToArray()
    {
        return (byte[])_attributes.Clone();
    }

    public ReadOnlySpan<byte> Attributes
    {
        get { return _attributes; }
    }

    public static AttributeGrid FromBytes(int columns, int rows, ReadOnlySpan<byte> data)
    {
        if (data.Length != columns * rows)
        {
            throw new ArgumentException(
                "Αναμένονταν " + (columns * rows) + " attributes, δόθηκαν " + data.Length + ".",
                nameof(data));
        }

        var grid = new AttributeGrid(columns, rows);
        data.CopyTo(grid._attributes);
        return grid;
    }

    private void EnsureInBounds(int column, int row)
    {
        if (column < 0 || column >= Columns)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Στήλη 0–" + (Columns - 1) + ".");
        }

        if (row < 0 || row >= Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, "Γραμμή 0–" + (Rows - 1) + ".");
        }
    }
}
