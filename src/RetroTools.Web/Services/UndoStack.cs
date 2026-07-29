namespace RetroTools.Web.Services;

/// <summary>Μία πινελιά: ποια pixels άλλαξαν, από τι και σε τι.</summary>
/// <remarks>
/// Αποθηκεύονται μόνο τα pixels που άλλαξαν, όχι ολόκληρο το καρέ. Ένα 128×128
/// sprite είναι 16 KB· με 100 βήματα undo θα κρατούσαμε 1.6 MB ανά ανοιχτό sprite,
/// πολλαπλασιασμένο με κάθε ταυτόχρονο χρήστη.
/// </remarks>
public sealed class StrokeCommand
{
    public StrokeCommand(int[] indices, byte[] newValues, byte[] previousValues)
    {
        if (indices.Length != newValues.Length || indices.Length != previousValues.Length)
        {
            throw new ArgumentException("Οι τρεις πίνακες πρέπει να έχουν ίδιο μήκος.", nameof(indices));
        }

        Indices = indices;
        NewValues = newValues;
        PreviousValues = previousValues;
    }

    public int[] Indices { get; }

    public byte[] NewValues { get; }

    public byte[] PreviousValues { get; }

    public int PixelCount
    {
        get { return Indices.Length; }
    }

    public void Apply(byte[] pixels)
    {
        for (var i = 0; i < Indices.Length; i++)
        {
            pixels[Indices[i]] = NewValues[i];
        }
    }

    public void Revert(byte[] pixels)
    {
        for (var i = 0; i < Indices.Length; i++)
        {
            pixels[Indices[i]] = PreviousValues[i];
        }
    }
}

/// <summary>
/// Ιστορικό undo/redo με άνω όριο, ώστε μια μακρά συνεδρία σχεδίασης να μη
/// φουσκώνει τη μνήμη του server επ' άπειρον.
/// </summary>
public sealed class UndoStack
{
    private readonly List<StrokeCommand> _undo = new List<StrokeCommand>();
    private readonly List<StrokeCommand> _redo = new List<StrokeCommand>();
    private readonly int _capacity;

    public UndoStack(int capacity = 100)
    {
        _capacity = capacity;
    }

    public bool CanUndo
    {
        get { return _undo.Count > 0; }
    }

    public bool CanRedo
    {
        get { return _redo.Count > 0; }
    }

    public int Depth
    {
        get { return _undo.Count; }
    }

    public void Push(StrokeCommand command)
    {
        _undo.Add(command);

        // Νέα ενέργεια μετά από undo ακυρώνει το «μπροστά» ιστορικό.
        _redo.Clear();

        if (_undo.Count > _capacity)
        {
            _undo.RemoveAt(0);
        }
    }

    public StrokeCommand? Undo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        var command = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(command);

        return command;
    }

    public StrokeCommand? Redo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var command = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(command);

        return command;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
