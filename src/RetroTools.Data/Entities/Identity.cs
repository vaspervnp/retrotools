namespace RetroTools.Data.Entities;

/// <summary>
/// Λογαριασμός χρήστη. Δεν αποθηκεύονται ποτέ κωδικοί: η ταυτοποίηση γίνεται
/// αποκλειστικά μέσω εξωτερικών providers (GitHub, Google).
/// </summary>
public sealed class User
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? AvatarUrl { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? LastLoginUtc { get; set; }

    public bool IsDisabled { get; set; }

    public ICollection<UserLogin> Logins { get; set; } = new List<UserLogin>();

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}

/// <summary>
/// Σύνδεση λογαριασμού με έναν εξωτερικό provider. Ένας χρήστης μπορεί να έχει
/// και GitHub και Google δεμένα στον ίδιο λογαριασμό.
/// </summary>
public sealed class UserLogin
{
    public const string GitHub = "github";
    public const string Google = "google";

    /// <summary>"github" ή "google".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Το σταθερό αναγνωριστικό του χρήστη στον provider (subject id).</summary>
    public string ProviderKey { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public DateTime LinkedUtc { get; set; }
}
