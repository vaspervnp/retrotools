using Microsoft.EntityFrameworkCore;
using RetroTools.Data.Entities;
using RetroTools.Data.Services;

namespace RetroTools.Data.Tests;

[Collection(DatabaseCollection.Name)]
public class UserProvisioningTests
{
    private readonly DatabaseFixture _fixture;

    public UserProvisioningTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static string UniqueKey()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static string UniqueEmail()
    {
        return Guid.NewGuid().ToString("N") + "@example.test";
    }

    private static ExternalLoginInfo Login(
        string provider,
        string providerKey,
        string? email = null,
        string displayName = "Δοκιμαστής",
        string? avatar = null)
    {
        return new ExternalLoginInfo(provider, providerKey, displayName, email, avatar);
    }

    private async Task<UserProvisioningResult> SignInAsync(ExternalLoginInfo info)
    {
        await using var context = _fixture.CreateContext(null);
        var service = new UserProvisioningService(context);
        var result = await service.SignInAsync(info);

        if (result.User != null)
        {
            _fixture.Track(result.User.Id);
        }

        return result;
    }

    // --- Δημιουργία & επανασύνδεση -------------------------------------------

    [DatabaseFact]
    public async Task First_sign_in_creates_an_account()
    {
        var key = UniqueKey();
        var result = await SignInAsync(Login(UserLogin.GitHub, key, UniqueEmail(), "Νίκος", "https://avatar.test/1.png"));

        Assert.Equal(UserProvisioningOutcome.Created, result.Outcome);
        Assert.NotNull(result.User);
        Assert.Equal("Νίκος", result.User!.DisplayName);
        Assert.Equal("https://avatar.test/1.png", result.User.AvatarUrl);
        Assert.NotNull(result.User.LastLoginUtc);

        await using var context = _fixture.CreateSystemContext();
        var login = await context.UserLogins
            .SingleOrDefaultAsync(l => l.Provider == UserLogin.GitHub && l.ProviderKey == key);

        Assert.NotNull(login);
        Assert.Equal(result.User.Id, login!.UserId);
    }

    [DatabaseFact]
    public async Task Second_sign_in_reuses_the_same_account()
    {
        var key = UniqueKey();
        var email = UniqueEmail();

        var first = await SignInAsync(Login(UserLogin.Google, key, email));
        var second = await SignInAsync(Login(UserLogin.Google, key, email));

        Assert.Equal(UserProvisioningOutcome.Created, first.Outcome);
        Assert.Equal(UserProvisioningOutcome.SignedIn, second.Outcome);
        Assert.Equal(first.User!.Id, second.User!.Id);
    }

    [DatabaseFact]
    public async Task Profile_changes_at_the_provider_are_picked_up()
    {
        var key = UniqueKey();

        await SignInAsync(Login(UserLogin.GitHub, key, UniqueEmail(), "Παλιό όνομα"));
        var updated = await SignInAsync(Login(UserLogin.GitHub, key, UniqueEmail(), "Νέο όνομα", "https://avatar.test/2.png"));

        Assert.Equal("Νέο όνομα", updated.User!.DisplayName);
        Assert.Equal("https://avatar.test/2.png", updated.User.AvatarUrl);
    }

    /// <summary>
    /// Το email δεν αντικαθίσταται σε επόμενες συνδέσεις: αν ο χρήστης το αλλάξει
    /// στον provider, μια σιωπηλή ενημέρωση θα μπορούσε να συγκρουστεί με το email
    /// άλλου λογαριασμού και να μπλοκάρει τη σύνδεσή του.
    /// </summary>
    [DatabaseFact]
    public async Task Existing_email_is_not_overwritten_on_later_sign_ins()
    {
        var key = UniqueKey();
        var original = UniqueEmail();

        await SignInAsync(Login(UserLogin.Google, key, original));
        var second = await SignInAsync(Login(UserLogin.Google, key, UniqueEmail()));

        Assert.Equal(original, second.User!.Email);
    }

    // --- Ασφάλεια: καμία αυτόματη σύνδεση λογαριασμών ------------------------

    /// <summary>
    /// Το βασικό μέτρο ασφαλείας: ένα δεύτερο provider με το ίδιο email <b>δεν</b>
    /// μπαίνει αυτόματα στον υπάρχοντα λογαριασμό. Αλλιώς θα αρκούσε κάποιος να
    /// δηλώσει το email του θύματος σε έναν provider που δεν το επαληθεύει.
    /// </summary>
    [DatabaseFact]
    public async Task Same_email_from_a_different_provider_does_not_auto_link()
    {
        var email = UniqueEmail();

        var created = await SignInAsync(Login(UserLogin.Google, UniqueKey(), email));
        var conflict = await SignInAsync(Login(UserLogin.GitHub, UniqueKey(), email));

        Assert.Equal(UserProvisioningOutcome.Created, created.Outcome);
        Assert.Equal(UserProvisioningOutcome.EmailBelongsToAnotherAccount, conflict.Outcome);
        Assert.Null(conflict.User);
        Assert.Equal(new[] { UserLogin.Google }, conflict.ExistingProviders);

        // Δεν δημιουργήθηκε δεύτερος λογαριασμός με το ίδιο email.
        await using var context = _fixture.CreateSystemContext();
        Assert.Equal(1, await context.Users.CountAsync(u => u.Email == email));
    }

    [DatabaseFact]
    public async Task Different_emails_create_separate_accounts()
    {
        var first = await SignInAsync(Login(UserLogin.Google, UniqueKey(), UniqueEmail()));
        var second = await SignInAsync(Login(UserLogin.GitHub, UniqueKey(), UniqueEmail()));

        Assert.NotEqual(first.User!.Id, second.User!.Id);
    }

    [DatabaseFact]
    public async Task Accounts_without_an_email_never_collide()
    {
        var first = await SignInAsync(Login(UserLogin.GitHub, UniqueKey()));
        var second = await SignInAsync(Login(UserLogin.GitHub, UniqueKey()));

        Assert.Equal(UserProvisioningOutcome.Created, first.Outcome);
        Assert.Equal(UserProvisioningOutcome.Created, second.Outcome);
        Assert.NotEqual(first.User!.Id, second.User!.Id);
    }

    // --- Ρητή σύνδεση providers ---------------------------------------------

    [DatabaseFact]
    public async Task Signed_in_user_can_link_a_second_provider()
    {
        var email = UniqueEmail();
        var account = await SignInAsync(Login(UserLogin.Google, UniqueKey(), email));
        var githubKey = UniqueKey();

        await using (var context = _fixture.CreateContext(null))
        {
            var service = new UserProvisioningService(context);
            Assert.True(await service.LinkAsync(account.User!.Id, Login(UserLogin.GitHub, githubKey, email)));
        }

        // Πλέον η σύνδεση με GitHub οδηγεί στον ίδιο λογαριασμό.
        var viaGitHub = await SignInAsync(Login(UserLogin.GitHub, githubKey, email));

        Assert.Equal(UserProvisioningOutcome.SignedIn, viaGitHub.Outcome);
        Assert.Equal(account.User!.Id, viaGitHub.User!.Id);
    }

    [DatabaseFact]
    public async Task Linking_an_identity_that_belongs_to_someone_else_fails()
    {
        var alice = await SignInAsync(Login(UserLogin.Google, UniqueKey(), UniqueEmail()));
        var bobKey = UniqueKey();
        await SignInAsync(Login(UserLogin.GitHub, bobKey, UniqueEmail()));

        await using var context = _fixture.CreateContext(null);
        var service = new UserProvisioningService(context);

        Assert.False(await service.LinkAsync(alice.User!.Id, Login(UserLogin.GitHub, bobKey)));
    }

    /// <summary>
    /// Αφαίρεση του τελευταίου provider θα άφηνε λογαριασμό χωρίς κανέναν τρόπο
    /// σύνδεσης — δηλαδή μόνιμα κλειδωμένα δεδομένα.
    /// </summary>
    [DatabaseFact]
    public async Task The_last_provider_cannot_be_unlinked()
    {
        var account = await SignInAsync(Login(UserLogin.GitHub, UniqueKey(), UniqueEmail()));

        await using var context = _fixture.CreateContext(null);
        var service = new UserProvisioningService(context);

        Assert.False(await service.UnlinkAsync(account.User!.Id, UserLogin.GitHub));
    }

    [DatabaseFact]
    public async Task A_provider_can_be_unlinked_when_another_remains()
    {
        var email = UniqueEmail();
        var account = await SignInAsync(Login(UserLogin.Google, UniqueKey(), email));

        await using var context = _fixture.CreateContext(null);
        var service = new UserProvisioningService(context);

        await service.LinkAsync(account.User!.Id, Login(UserLogin.GitHub, UniqueKey(), email));

        Assert.True(await service.UnlinkAsync(account.User.Id, UserLogin.GitHub));
        Assert.False(await service.UnlinkAsync(account.User.Id, UserLogin.Google));
    }

    // --- Σύνδεση με τα φίλτρα ιδιοκτησίας ------------------------------------

    /// <summary>
    /// Ολοκληρωμένη διαδρομή: ο χρήστης συνδέεται, φτιάχνει project, και ένας
    /// δεύτερος χρήστης που μπήκε από άλλον provider δεν το βλέπει.
    /// </summary>
    [DatabaseFact]
    public async Task Provisioned_users_get_isolated_data()
    {
        var alice = await SignInAsync(Login(UserLogin.GitHub, UniqueKey(), UniqueEmail(), "Alice"));
        var bob = await SignInAsync(Login(UserLogin.Google, UniqueKey(), UniqueEmail(), "Bob"));

        long projectId;

        await using (var context = _fixture.CreateContext(alice.User!.Id))
        {
            var project = new Project
            {
                OwnerId = alice.User.Id,
                Name = "Της Alice",
                PlatformCode = "c64",
                ModeCode = "c64.sprite_multicolor",
            };

            context.Projects.Add(project);
            await context.SaveChangesAsync();
            projectId = project.Id;
        }

        await using var bobContext = _fixture.CreateContext(bob.User!.Id);
        Assert.Null(await bobContext.Projects.SingleOrDefaultAsync(p => p.Id == projectId));
    }
}
