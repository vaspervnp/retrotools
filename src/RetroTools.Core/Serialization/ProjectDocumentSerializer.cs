using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetroTools.Core.Serialization;

/// <summary>Το αποτέλεσμα μιας απόπειρας ανάγνωσης εγγράφου project.</summary>
public sealed class ProjectDocumentReadResult
{
    private ProjectDocumentReadResult(ProjectDocument? document, IReadOnlyList<string> errors)
    {
        Document = document;
        Errors = errors;
    }

    public ProjectDocument? Document { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool Success
    {
        get { return Document != null && Errors.Count == 0; }
    }

    internal static ProjectDocumentReadResult Ok(ProjectDocument document)
    {
        return new ProjectDocumentReadResult(document, Array.Empty<string>());
    }

    internal static ProjectDocumentReadResult Failed(IReadOnlyList<string> errors)
    {
        return new ProjectDocumentReadResult(null, errors);
    }
}

public static class ProjectDocumentSerializer
{
    /// <summary>
    /// Το αρχείο προορίζεται και για ανθρώπινη ανάγνωση και για git diff, οπότε
    /// γράφεται με εσοχές. Τα ελληνικά δεν γίνονται escape σε <c>\uXXXX</c>.
    /// </summary>
    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static byte[] Write(ProjectDocument document)
    {
        return JsonSerializer.SerializeToUtf8Bytes(Stamp(document), WriteOptions);
    }

    public static string WriteToString(ProjectDocument document)
    {
        return JsonSerializer.Serialize(Stamp(document), WriteOptions);
    }

    /// <summary>
    /// Σφραγίζει τον «φάκελο» του εγγράφου. Γίνεται εδώ και όχι με initializer στο
    /// μοντέλο, ώστε ένα εισερχόμενο αρχείο που δεν δηλώνει μορφή και έκδοση να
    /// απορρίπτεται αντί να τα αποκτά σιωπηλά.
    /// </summary>
    private static ProjectDocument Stamp(ProjectDocument document)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        document.Format = ProjectDocument.FormatIdentifier;
        document.Version = ProjectDocument.CurrentVersion;

        return document;
    }

    /// <summary>
    /// Διαβάζει και επικυρώνει. Δεν πετάει εξαίρεση για κακό αρχείο — επιστρέφει
    /// τα σφάλματα, ώστε το UI να μπορεί να τα δείξει όλα μαζί στον χρήστη αντί
    /// για ένα κάθε φορά.
    /// </summary>
    public static ProjectDocumentReadResult Read(ReadOnlySpan<byte> utf8Json)
    {
        ProjectDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<ProjectDocument>(utf8Json, ReadOptions);
        }
        catch (JsonException exception)
        {
            return ProjectDocumentReadResult.Failed(new[] { "Μη έγκυρο JSON: " + exception.Message });
        }

        var errors = ProjectDocumentValidator.Validate(document);

        return errors.Count == 0
            ? ProjectDocumentReadResult.Ok(document!)
            : ProjectDocumentReadResult.Failed(errors);
    }
}
