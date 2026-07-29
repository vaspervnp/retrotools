using Microsoft.EntityFrameworkCore;
using RetroTools.Core.Codecs;
using RetroTools.Core.Model;
using RetroTools.Core.Platforms;
using RetroTools.Core.Serialization;
using RetroTools.Data.Entities;
using RetroTools.Data.Seeding;

namespace RetroTools.Data.Tests;

[Collection(DatabaseCollection.Name)]
public class PersistenceTests
{
    private readonly DatabaseFixture _fixture;

    public PersistenceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(Guid UserId, long ProjectId)> CreateProjectAsync(
        string platformCode = "cpc",
        string modeCode = "cpc.mode0")
    {
        var user = await _fixture.CreateUserAsync("Δοκιμαστής");

        await using var context = _fixture.CreateContext(user.Id);

        var project = new Project
        {
            OwnerId = user.Id,
            Name = "Δοκιμή " + Guid.NewGuid().ToString("N").Substring(0, 8),
            PlatformCode = platformCode,
            ModeCode = modeCode,
        };

        context.Projects.Add(project);
        await context.SaveChangesAsync();

        return (user.Id, project.Id);
    }

    // --- Seeding -------------------------------------------------------------

    [DatabaseFact]
    public async Task Platform_catalog_is_seeded_and_matches_the_code()
    {
        await using var context = _fixture.CreateSystemContext();
        await PlatformSeeder.SeedAsync(context);

        var platforms = await context.Platforms.ToListAsync();
        var modes = await context.PlatformModes.ToListAsync();

        Assert.Equal(PlatformCatalog.All.Count, platforms.Count);
        Assert.Equal(PlatformCatalog.AllModes.Count, modes.Count);

        var cpc = platforms.Single(p => p.Code == "cpc");
        Assert.Equal(27, cpc.ColorCount);
        Assert.True(cpc.HasProgrammablePalette);
        Assert.False(cpc.HasHardwareSprites);

        var spriteMode = modes.Single(m => m.Code == "c64.sprite_hires");
        Assert.Equal(24, spriteMode.FixedWidth);
        Assert.Equal(21, spriteMode.FixedHeight);
        Assert.True(spriteMode.IsHardwareSprite);
    }

    [DatabaseFact]
    public async Task Seeding_twice_does_not_duplicate_rows()
    {
        await using var context = _fixture.CreateSystemContext();

        await PlatformSeeder.SeedAsync(context);
        var firstCount = await context.PlatformModes.CountAsync();

        var changes = await PlatformSeeder.SeedAsync(context);

        Assert.Equal(0, changes);
        Assert.Equal(firstCount, await context.PlatformModes.CountAsync());
    }

    // --- Δεδομένα pixel ------------------------------------------------------

    /// <summary>
    /// Ο πραγματικός κίνδυνος με BLOB πάνω από MySQL είναι σιωπηλή αλλοίωση
    /// (charset, padding, truncation). Εδώ αποθηκεύεται ένα καρέ με όλες τις
    /// τιμές 0–15 και επαληθεύεται byte προς byte μετά την ανάγνωση.
    /// </summary>
    [DatabaseFact]
    public async Task Sprite_frame_survives_a_full_database_round_trip()
    {
        var (userId, projectId) = await CreateProjectAsync();

        var original = new FrameBuffer(16, 16);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                original[x, y] = (byte)((x + y) % 16);
            }
        }

        long frameId;

        await using (var context = _fixture.CreateContext(userId))
        {
            var sprite = new Sprite
            {
                ProjectId = projectId,
                Name = "Παίκτης",
                WidthPx = 16,
                HeightPx = 16,
                MetaJson = "{\"c64SpriteColor\":1}",
            };

            sprite.Frames.Add(new SpriteFrame
            {
                FrameIndex = 0,
                DurationMs = 80,
                PixelData = RsprContainer.Write(original),
            });

            context.Sprites.Add(sprite);
            await context.SaveChangesAsync();
            frameId = sprite.Frames.First().Id;
        }

        await using var reader = _fixture.CreateContext(userId);
        var stored = await reader.SpriteFrames.SingleAsync(f => f.Id == frameId);
        var restored = RsprContainer.Read(stored.PixelData);

        Assert.True(original.HasSamePixels(restored));
        Assert.Equal(80, stored.DurationMs);
    }

    /// <summary>
    /// Ολοκληρωμένη διαδρομή: ζωγραφίζω → αποθηκεύω → φορτώνω → εξάγω bytes CPC.
    /// Αν κάτι χαλάσει οπουδήποτε στην αλυσίδα, το τελικό byte θα διαφέρει.
    /// </summary>
    [DatabaseFact]
    public async Task Round_trip_through_the_database_produces_identical_hardware_bytes()
    {
        var (userId, projectId) = await CreateProjectAsync();
        var codec = SpriteCodecs.For("cpc.mode0");

        var frame = new FrameBuffer(4, 2);
        frame[0, 0] = 15;
        frame[1, 0] = 0;
        frame[2, 0] = 8;
        frame[3, 0] = 1;
        frame[0, 1] = 2;
        frame[1, 1] = 4;

        var expectedBytes = codec.Pack(frame);
        long spriteId;

        await using (var context = _fixture.CreateContext(userId))
        {
            var sprite = new Sprite { ProjectId = projectId, Name = "Bytes", WidthPx = 4, HeightPx = 2 };
            sprite.Frames.Add(new SpriteFrame { FrameIndex = 0, PixelData = RsprContainer.Write(frame) });
            context.Sprites.Add(sprite);
            await context.SaveChangesAsync();
            spriteId = sprite.Id;
        }

        await using var reader = _fixture.CreateContext(userId);
        var loaded = await reader.Sprites.Include(s => s.Frames).SingleAsync(s => s.Id == spriteId);
        var loadedFrame = RsprContainer.Read(loaded.Frames.Single().PixelData);

        Assert.Equal(expectedBytes, codec.Pack(loadedFrame));
        Assert.Equal(0xAA, expectedBytes[0]); // pen 15 αριστερά, 0 δεξιά
    }

    [DatabaseFact]
    public async Task Attribute_and_mask_blobs_are_optional_and_persist()
    {
        var (userId, projectId) = await CreateProjectAsync("zx", "zx.sprite");

        var mask = new FrameBuffer(16, 16);
        mask.Fill(1);

        var attributes = AttributeGrid.ForSprite(16, 16);
        attributes.SetCell(0, 0, ink: 6, paper: 1, bright: true, flash: false);

        long spriteId;

        await using (var context = _fixture.CreateContext(userId))
        {
            var sprite = new Sprite
            {
                ProjectId = projectId,
                Name = "Με μάσκα",
                WidthPx = 16,
                HeightPx = 16,
                HasMask = true,
            };

            sprite.Frames.Add(new SpriteFrame
            {
                FrameIndex = 0,
                PixelData = RsprContainer.Write(new FrameBuffer(16, 16)),
                AttributeData = attributes.ToArray(),
                MaskData = RsprContainer.Write(mask),
            });

            context.Sprites.Add(sprite);
            await context.SaveChangesAsync();
            spriteId = sprite.Id;
        }

        await using var reader = _fixture.CreateContext(userId);
        var stored = await reader.SpriteFrames.SingleAsync(f => f.SpriteId == spriteId);

        Assert.NotNull(stored.AttributeData);
        Assert.NotNull(stored.MaskData);

        var restoredAttributes = AttributeGrid.FromBytes(2, 2, stored.AttributeData!);
        Assert.Equal((6, 1, true, false), restoredAttributes.ReadCell(0, 0));
        Assert.True(RsprContainer.Read(stored.MaskData!).HasSamePixels(mask));
    }

    // --- Ακεραιότητα σχέσεων -------------------------------------------------

    [DatabaseFact]
    public async Task Deleting_a_project_cascades_to_sprites_and_frames()
    {
        var (userId, projectId) = await CreateProjectAsync();

        await using (var context = _fixture.CreateContext(userId))
        {
            var sprite = new Sprite { ProjectId = projectId, Name = "Προσωρινό", WidthPx = 8, HeightPx = 8 };
            sprite.Frames.Add(new SpriteFrame { FrameIndex = 0, PixelData = RsprContainer.Write(new FrameBuffer(8, 8)) });
            context.Sprites.Add(sprite);
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext(userId))
        {
            var project = await context.Projects.SingleAsync(p => p.Id == projectId);
            context.Projects.Remove(project);
            await context.SaveChangesAsync();
        }

        await using var check = _fixture.CreateSystemContext();
        Assert.Empty(await check.Sprites.Where(s => s.ProjectId == projectId).ToListAsync());
        Assert.Empty(await check.SpriteFrames.Where(f => f.Sprite!.ProjectId == projectId).ToListAsync());
    }

    /// <summary>
    /// Η διαγραφή ομάδας δεν πρέπει να παρασύρει τα sprites: ο χρήστης οργανώνει
    /// ξανά, δεν χάνει δουλειά.
    /// </summary>
    [DatabaseFact]
    public async Task Deleting_a_group_leaves_its_sprites_untouched()
    {
        var (userId, projectId) = await CreateProjectAsync();
        long spriteId;

        await using (var context = _fixture.CreateContext(userId))
        {
            var group = new SpriteGroup { ProjectId = projectId, Name = "Προσωρινή ομάδα" };
            context.SpriteGroups.Add(group);
            await context.SaveChangesAsync();

            var sprite = new Sprite
            {
                ProjectId = projectId,
                GroupId = group.Id,
                Name = "Επιζών",
                WidthPx = 8,
                HeightPx = 8,
            };

            context.Sprites.Add(sprite);
            await context.SaveChangesAsync();
            spriteId = sprite.Id;

            context.SpriteGroups.Remove(group);
            await context.SaveChangesAsync();
        }

        await using var reader = _fixture.CreateContext(userId);
        var survivor = await reader.Sprites.SingleOrDefaultAsync(s => s.Id == spriteId);

        Assert.NotNull(survivor);
        Assert.Null(survivor!.GroupId);
    }

    [DatabaseFact]
    public async Task Frame_index_must_be_unique_within_a_sprite()
    {
        var (userId, projectId) = await CreateProjectAsync();

        await using var context = _fixture.CreateContext(userId);

        var sprite = new Sprite { ProjectId = projectId, Name = "Διπλό καρέ", WidthPx = 8, HeightPx = 8 };
        sprite.Frames.Add(new SpriteFrame { FrameIndex = 0, PixelData = RsprContainer.Write(new FrameBuffer(8, 8)) });
        sprite.Frames.Add(new SpriteFrame { FrameIndex = 0, PixelData = RsprContainer.Write(new FrameBuffer(8, 8)) });

        context.Sprites.Add(sprite);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    // --- Audit & concurrency -------------------------------------------------

    [DatabaseFact]
    public async Task Timestamps_and_row_version_are_maintained_automatically()
    {
        var (userId, projectId) = await CreateProjectAsync();

        await using var context = _fixture.CreateContext(userId);
        var project = await context.Projects.SingleAsync(p => p.Id == projectId);

        Assert.NotEqual(default, project.CreatedUtc);
        Assert.Equal(1, project.RowVersion);

        project.Name = "Μετονομασμένο";
        await context.SaveChangesAsync();

        Assert.Equal(2, project.RowVersion);
        Assert.True(project.UpdatedUtc >= project.CreatedUtc);
    }

    /// <summary>
    /// Δύο ταυτόχρονες καρτέλες του editor δεν πρέπει να πατάει η μία τη δουλειά
    /// της άλλης σιωπηλά.
    /// </summary>
    [DatabaseFact]
    public async Task Concurrent_edits_are_detected()
    {
        var (userId, projectId) = await CreateProjectAsync();

        await using var first = _fixture.CreateContext(userId);
        await using var second = _fixture.CreateContext(userId);

        var fromFirst = await first.Projects.SingleAsync(p => p.Id == projectId);
        var fromSecond = await second.Projects.SingleAsync(p => p.Id == projectId);

        fromFirst.Name = "Άλλαξε από την πρώτη";
        await first.SaveChangesAsync();

        fromSecond.Name = "Άλλαξε από τη δεύτερη";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    // --- Unicode & collation -------------------------------------------------

    [DatabaseFact]
    public async Task Greek_and_emoji_names_survive_utf8mb4()
    {
        var user = await _fixture.CreateUserAsync("Χρήστης 🎮");

        await using var context = _fixture.CreateContext(user.Id);

        var project = new Project
        {
            OwnerId = user.Id,
            Name = "Παιχνίδι 🕹️ Νο.1",
            Description = "Ελληνικά, ñ, 中文, 🎨",
            PlatformCode = "zx",
            ModeCode = "zx.sprite",
        };

        context.Projects.Add(project);
        await context.SaveChangesAsync();

        await using var reader = _fixture.CreateContext(user.Id);
        var stored = await reader.Projects.SingleAsync(p => p.Id == project.Id);

        Assert.Equal("Παιχνίδι 🕹️ Νο.1", stored.Name);
        Assert.Equal("Ελληνικά, ñ, 中文, 🎨", stored.Description);
    }
}
