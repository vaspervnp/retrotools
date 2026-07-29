using Microsoft.AspNetCore.Mvc;

namespace RetroTools.Web.Controllers;

/// <summary>
/// Κοινή βάση για τα API controllers.
/// </summary>
public abstract class RetroApiController : ControllerBase
{
    /// <summary>
    /// Απάντηση για αντικείμενο που δεν υπάρχει <b>ή</b> δεν ανήκει στον χρήστη.
    /// </summary>
    /// <remarks>
    /// Επιστρέφεται σκόπιμα <b>404 και όχι 403</b>: ένα 403 θα επιβεβαίωνε ότι το
    /// αντικείμενο υπάρχει, επιτρέποντας σε κάποιον να απαριθμήσει ids και να μάθει
    /// πόσα projects έχουν οι άλλοι χρήστες. Το 404 δεν διαρρέει τίποτα.
    /// </remarks>
    protected ActionResult NotFoundOrForbidden(string what)
    {
        return Problem(
            title: "Δεν βρέθηκε",
            detail: what + " δεν υπάρχει ή δεν έχεις πρόσβαση.",
            statusCode: StatusCodes.Status404NotFound);
    }

    protected ActionResult Conflict(string title, string detail)
    {
        return Problem(title: title, detail: detail, statusCode: StatusCodes.Status409Conflict);
    }

    protected ActionResult InvalidRequest(string title, string detail)
    {
        return Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Αποκωδικοποιεί base64 με κατανοητό μήνυμα αντί για γενικό 400 του model binder.
    /// </summary>
    protected static bool TryDecodeBase64(string? value, out byte[] data, out string? error)
    {
        data = Array.Empty<byte>();
        error = null;

        if (string.IsNullOrEmpty(value))
        {
            error = "Το πεδίο είναι κενό.";
            return false;
        }

        try
        {
            data = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            error = "Τα δεδομένα δεν είναι έγκυρο base64.";
            return false;
        }
    }
}
