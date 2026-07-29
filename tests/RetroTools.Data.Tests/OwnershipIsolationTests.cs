using Microsoft.EntityFrameworkCore;
using RetroTools.Core.Model;
using RetroTools.Core.Serialization;
using RetroTools.Data.Entities;

namespace RetroTools.Data.Tests;

/// <summary>
/// Η πιο σημαντική συμπεριφορά ασφαλείας της εφαρμογής: ο χρήστης Α δεν πρέπει
/// να μπορεί να δει τίποτα του χρήστη Β, ακόμη κι αν ο κώδικας ξεχάσει ένα <c>Where</c>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class OwnershipIsolationTests
{
    private readonly DatabaseFixture _fixture;

    public OwnershipIsolationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(User Alice, User Bob, long AliceProjectId)> SetupTwoUsersAsync()
    {
        var alice = await _fixture.CreateUserAsync("Alice");
        var bob = await _fixture.CreateUserAsync("Bob");

        await using var context = _fixture.CreateContext(alice.Id);

        var project = new Project
        {
            OwnerId = alice.Id,
            Name = "Το project της Alice",
            PlatformCode = "cpc",
            ModeCode = "cpc.mode0",
        };

        context.Projects.Add(project);
        await context.SaveChangesAsync();

        return (alice, bob, project.Id);
    }

    [DatabaseFact]
    public async Task Owner_sees_their_own_project()
    {
        var (alice, _, projectId) = await SetupTwoUsersAsync();

        await using var context = _fixture.CreateContext(alice.Id);
        var project = await context.Projects.SingleOrDefaultAsync(p => p.Id == projectId);

        Assert.NotNull(project);
        Assert.Equal("Το project της Alice", project!.Name);
    }

    [DatabaseFact]
    public async Task Other_user_cannot_see_the_project_even_by_id()
    {
        var (_, bob, projectId) = await SetupTwoUsersAsync();

        await using var context = _fixture.CreateContext(bob.Id);

        Assert.Null(await context.Projects.SingleOrDefaultAsync(p => p.Id == projectId));
        Assert.Empty(await context.Projects.Where(p => p.Id == projectId).ToListAsync());
    }

    [DatabaseFact]
    public async Task Anonymous_visitor_sees_nothing_private()
    {
        var (_, _, projectId) = await SetupTwoUsersAsync();

        await using var context = _fixture.CreateContext(null);

        Assert.Null(await context.Projects.SingleOrDefaultAsync(p => p.Id == projectId));
    }

    [DatabaseFact]
    public async Task Public_projects_are_visible_to_everyone()
    {
        var (alice, bob, projectId) = await SetupTwoUsersAsync();

        await using (var owner = _fixture.CreateContext(alice.Id))
        {
            var project = await owner.Projects.SingleAsync(p => p.Id == projectId);
            project.Visibility = ProjectVisibility.Public;
            await owner.SaveChangesAsync();
        }

        await using var other = _fixture.CreateContext(bob.Id);
        await using var anonymous = _fixture.CreateContext(null);

        Assert.NotNull(await other.Projects.SingleOrDefaultAsync(p => p.Id == projectId));
        Assert.NotNull(await anonymous.Projects.SingleOrDefaultAsync(p => p.Id == projectId));
    }

    /// <summary>
    /// Το φίλτρο δεν είναι μόνο στο <c>projects</c>. Ένα ερώτημα κατευθείαν στα
    /// sprites ή στα καρέ πρέπει να είναι εξίσου τυφλό στα ξένα δεδομένα —
    /// αλλιώς ένα endpoint που δέχεται <c>spriteId</c> θα διέρρεε δουλειά άλλου χρήστη.
    /// </summary>
    [DatabaseFact]
    public async Task Sprites_and_frames_are_filtered_too_not_just_projects()
    {
        var (alice, bob, projectId) = await SetupTwoUsersAsync();

        long spriteId;
        long frameId;

        await using (var owner = _fixture.CreateContext(alice.Id))
        {
            var sprite = new Sprite
            {
                ProjectId = projectId,
                Name = "Μυστικό sprite",
                WidthPx = 16,
                HeightPx = 16,
            };

            sprite.Frames.Add(new SpriteFrame
            {
                FrameIndex = 0,
                PixelData = RsprContainer.Write(new FrameBuffer(16, 16)),
            });

            owner.Sprites.Add(sprite);
            await owner.SaveChangesAsync();

            spriteId = sprite.Id;
            frameId = sprite.Frames.First().Id;
        }

        await using var intruder = _fixture.CreateContext(bob.Id);

        Assert.Null(await intruder.Sprites.SingleOrDefaultAsync(s => s.Id == spriteId));
        Assert.Null(await intruder.SpriteFrames.SingleOrDefaultAsync(f => f.Id == frameId));
        Assert.Empty(await intruder.Sprites.ToListAsync());

        // Ο ιδιοκτήτης τα βλέπει κανονικά.
        await using var owner2 = _fixture.CreateContext(alice.Id);
        Assert.NotNull(await owner2.Sprites.SingleOrDefaultAsync(s => s.Id == spriteId));
        Assert.NotNull(await owner2.SpriteFrames.SingleOrDefaultAsync(f => f.Id == frameId));
    }

    [DatabaseFact]
    public async Task Spritemaps_groups_and_palettes_are_filtered()
    {
        var (alice, bob, projectId) = await SetupTwoUsersAsync();

        await using (var owner = _fixture.CreateContext(alice.Id))
        {
            owner.SpriteGroups.Add(new SpriteGroup { ProjectId = projectId, Name = "Εχθροί" });
            owner.SpriteMaps.Add(new SpriteMap
            {
                ProjectId = projectId,
                Name = "Tileset",
                Columns = 4,
                Rows = 4,
                CellWidthPx = 16,
                CellHeightPx = 16,
            });

            var palette = new Palette { ProjectId = projectId, Name = "Κύρια" };
            palette.Entries.Add(new PaletteEntry { SlotIndex = 0, HardwareColorIndex = 0 });
            owner.Palettes.Add(palette);

            await owner.SaveChangesAsync();
        }

        await using var intruder = _fixture.CreateContext(bob.Id);

        Assert.Empty(await intruder.SpriteGroups.ToListAsync());
        Assert.Empty(await intruder.SpriteMaps.ToListAsync());
        Assert.Empty(await intruder.Palettes.ToListAsync());
        Assert.Empty(await intruder.PaletteEntries.ToListAsync());
    }

    /// <summary>
    /// Το σύστημα (migrations, seeding, συντήρηση) πρέπει να μπορεί να παρακάμψει
    /// τα φίλτρα — αλλιώς δεν θα μπορούσε να καθαρίσει ή να μεταφέρει δεδομένα.
    /// </summary>
    [DatabaseFact]
    public async Task System_context_bypasses_the_filters()
    {
        var (_, _, projectId) = await SetupTwoUsersAsync();

        await using var system = _fixture.CreateSystemContext();

        Assert.NotNull(await system.Projects.SingleOrDefaultAsync(p => p.Id == projectId));
    }
}
