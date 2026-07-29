using Microsoft.EntityFrameworkCore;
using RetroTools.Data.Entities;

namespace RetroTools.Data;

public sealed class RetroToolsDbContext : DbContext
{
    private readonly Guid? _currentUserId;
    private readonly bool _bypassOwnershipFilters;

    public RetroToolsDbContext(DbContextOptions<RetroToolsDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUserId = currentUser?.UserId;
        _bypassOwnershipFilters = currentUser is SystemUser;
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<UserLogin> UserLogins => Set<UserLogin>();

    public DbSet<PlatformRecord> Platforms => Set<PlatformRecord>();

    public DbSet<PlatformModeRecord> PlatformModes => Set<PlatformModeRecord>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Palette> Palettes => Set<Palette>();

    public DbSet<PaletteEntry> PaletteEntries => Set<PaletteEntry>();

    public DbSet<SpriteGroup> SpriteGroups => Set<SpriteGroup>();

    public DbSet<Sprite> Sprites => Set<Sprite>();

    public DbSet<SpriteFrame> SpriteFrames => Set<SpriteFrame>();

    public DbSet<SpriteMap> SpriteMaps => Set<SpriteMap>();

    public DbSet<SpriteMapCell> SpriteMapCells => Set<SpriteMapCell>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ConfigureIdentity(builder);
        ConfigureCatalog(builder);
        ConfigureProjects(builder);
        ConfigureSprites(builder);
        ConfigureSpriteMaps(builder);
        ConfigureOwnershipFilters(builder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditAndConcurrency();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditAndConcurrency();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // --- Ρύθμιση μοντέλου ---------------------------------------------------

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DisplayName).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.AvatarUrl).HasMaxLength(512);
            entity.Property(e => e.CreatedUtc).HasColumnType("datetime(3)");
            entity.Property(e => e.LastLoginUtc).HasColumnType("datetime(3)");
            entity.HasIndex(e => e.Email);
        });

        builder.Entity<UserLogin>(entity =>
        {
            entity.ToTable("user_logins");
            entity.HasKey(e => new { e.Provider, e.ProviderKey });
            entity.Property(e => e.Provider).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ProviderKey).HasMaxLength(128).IsRequired();
            entity.Property(e => e.LinkedUtc).HasColumnType("datetime(3)");

            entity.HasOne(e => e.User)
                .WithMany(u => u.Logins)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCatalog(ModelBuilder builder)
    {
        builder.Entity<PlatformRecord>(entity =>
        {
            entity.ToTable("platforms");
            entity.HasKey(e => e.Code);
            entity.Property(e => e.Code).HasMaxLength(16);
            entity.Property(e => e.Name).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Manufacturer).HasMaxLength(64).IsRequired();
        });

        builder.Entity<PlatformModeRecord>(entity =>
        {
            entity.ToTable("platform_modes");
            entity.HasKey(e => e.Code);
            entity.Property(e => e.Code).HasMaxLength(32);
            entity.Property(e => e.PlatformCode).HasMaxLength(16).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(96).IsRequired();

            entity.HasOne(e => e.Platform)
                .WithMany(p => p.Modes)
                .HasForeignKey(e => e.PlatformCode)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureProjects(ModelBuilder builder)
    {
        builder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1024);
            entity.Property(e => e.PlatformCode).HasMaxLength(16).IsRequired();
            entity.Property(e => e.ModeCode).HasMaxLength(32).IsRequired();
            entity.Property(e => e.PaletteProfileId).HasMaxLength(32);
            entity.Property(e => e.Visibility).HasConversion<int>();
            entity.Property(e => e.CreatedUtc).HasColumnType("datetime(3)");
            entity.Property(e => e.UpdatedUtc).HasColumnType("datetime(3)");
            entity.Property(e => e.RowVersion).IsConcurrencyToken();

            entity.HasIndex(e => e.OwnerId);
            entity.HasIndex(e => new { e.OwnerId, e.Name });

            entity.HasOne(e => e.Owner)
                .WithMany(u => u.Projects)
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict: δεν διαγράφουμε πλατφόρμες όσο υπάρχουν projects πάνω τους.
            entity.HasOne(e => e.Platform)
                .WithMany()
                .HasForeignKey(e => e.PlatformCode)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Mode)
                .WithMany()
                .HasForeignKey(e => e.ModeCode)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Palette>(entity =>
        {
            entity.ToTable("palettes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(64).IsRequired();

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Palettes)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PaletteEntry>(entity =>
        {
            entity.ToTable("palette_entries");
            entity.HasKey(e => new { e.PaletteId, e.SlotIndex });

            entity.HasOne(e => e.Palette)
                .WithMany(p => p.Entries)
                .HasForeignKey(e => e.PaletteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SpriteGroup>(entity =>
        {
            entity.ToTable("sprite_groups");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Groups)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSprites(ModelBuilder builder)
    {
        builder.Entity<Sprite>(entity =>
        {
            entity.ToTable("sprites");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.MetaJson).HasColumnType("json");
            entity.Property(e => e.CreatedUtc).HasColumnType("datetime(3)");
            entity.Property(e => e.UpdatedUtc).HasColumnType("datetime(3)");
            entity.Property(e => e.RowVersion).IsConcurrencyToken();

            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.GroupId);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Sprites)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Η διαγραφή ομάδας δεν διαγράφει τα sprites — μένουν αταξινόμητα.
            entity.HasOne(e => e.Group)
                .WithMany(g => g.Sprites)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Palette)
                .WithMany()
                .HasForeignKey(e => e.PaletteId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SpriteFrame>(entity =>
        {
            entity.ToTable("sprite_frames");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PixelData).HasColumnType("MEDIUMBLOB").IsRequired();
            entity.Property(e => e.AttributeData).HasColumnType("MEDIUMBLOB");
            entity.Property(e => e.MaskData).HasColumnType("MEDIUMBLOB");

            entity.HasIndex(e => new { e.SpriteId, e.FrameIndex }).IsUnique();

            entity.HasOne(e => e.Sprite)
                .WithMany(s => s.Frames)
                .HasForeignKey(e => e.SpriteId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSpriteMaps(ModelBuilder builder)
    {
        builder.Entity<SpriteMap>(entity =>
        {
            entity.ToTable("spritemaps");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.CreatedUtc).HasColumnType("datetime(3)");
            entity.Property(e => e.UpdatedUtc).HasColumnType("datetime(3)");
            entity.Property(e => e.RowVersion).IsConcurrencyToken();

            entity.HasIndex(e => e.ProjectId);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.SpriteMaps)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SpriteMapCell>(entity =>
        {
            entity.ToTable("spritemap_cells");
            entity.HasKey(e => new { e.SpriteMapId, e.Column, e.Row });
            entity.Property(e => e.Flags).HasConversion<int>();

            entity.HasOne(e => e.SpriteMap)
                .WithMany(m => m.Cells)
                .HasForeignKey(e => e.SpriteMapId)
                .OnDelete(DeleteBehavior.Cascade);

            // Αν διαγραφεί ένα sprite, το κελί αδειάζει αντί να χαθεί ολόκληρο το spritemap.
            entity.HasOne(e => e.Sprite)
                .WithMany()
                .HasForeignKey(e => e.SpriteId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>
    /// Καθολικά φίλτρα ιδιοκτησίας. Δηλώνονται σε <b>κάθε</b> οντότητα του project,
    /// όχι μόνο στο <c>projects</c>: ένα ξεχασμένο <c>context.Sprites.ToList()</c>
    /// δεν πρέπει να μπορεί να δει δεδομένα άλλου χρήστη.
    /// </summary>
    private void ConfigureOwnershipFilters(ModelBuilder builder)
    {
        builder.Entity<Project>()
            .HasQueryFilter(p => _bypassOwnershipFilters
                                 || p.OwnerId == _currentUserId
                                 || p.Visibility == ProjectVisibility.Public);

        builder.Entity<Palette>()
            .HasQueryFilter(e => _bypassOwnershipFilters
                                 || e.Project!.OwnerId == _currentUserId
                                 || e.Project.Visibility == ProjectVisibility.Public);

        builder.Entity<PaletteEntry>()
            .HasQueryFilter(e => _bypassOwnershipFilters
                                 || e.Palette!.Project!.OwnerId == _currentUserId
                                 || e.Palette.Project.Visibility == ProjectVisibility.Public);

        builder.Entity<SpriteGroup>()
            .HasQueryFilter(e => _bypassOwnershipFilters
                                 || e.Project!.OwnerId == _currentUserId
                                 || e.Project.Visibility == ProjectVisibility.Public);

        builder.Entity<Sprite>()
            .HasQueryFilter(e => _bypassOwnershipFilters
                                 || e.Project!.OwnerId == _currentUserId
                                 || e.Project.Visibility == ProjectVisibility.Public);

        builder.Entity<SpriteFrame>()
            .HasQueryFilter(e => _bypassOwnershipFilters
                                 || e.Sprite!.Project!.OwnerId == _currentUserId
                                 || e.Sprite.Project.Visibility == ProjectVisibility.Public);

        builder.Entity<SpriteMap>()
            .HasQueryFilter(e => _bypassOwnershipFilters
                                 || e.Project!.OwnerId == _currentUserId
                                 || e.Project.Visibility == ProjectVisibility.Public);

        builder.Entity<SpriteMapCell>()
            .HasQueryFilter(e => _bypassOwnershipFilters
                                 || e.SpriteMap!.Project!.OwnerId == _currentUserId
                                 || e.SpriteMap.Project.Visibility == ProjectVisibility.Public);
    }

    private void ApplyAuditAndConcurrency()
    {
        var now = DateTime.UtcNow;

        // Οι οντότητες με χρονοσφραγίδες αντιμετωπίζονται ρητά ώστε να μην
        // χρειάζεται reflection σε κάθε αποθήκευση.
        foreach (var entry in ChangeTracker.Entries<Project>())
        {
            ApplyTimestamps(entry.State, now, e => entry.Entity.CreatedUtc = e, e => entry.Entity.UpdatedUtc = e);

            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                entry.Entity.RowVersion++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Sprite>())
        {
            ApplyTimestamps(entry.State, now, e => entry.Entity.CreatedUtc = e, e => entry.Entity.UpdatedUtc = e);

            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                entry.Entity.RowVersion++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<SpriteMap>())
        {
            ApplyTimestamps(entry.State, now, e => entry.Entity.CreatedUtc = e, e => entry.Entity.UpdatedUtc = e);

            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                entry.Entity.RowVersion++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedUtc = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<UserLogin>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.LinkedUtc = now;
            }
        }
    }

    private static void ApplyTimestamps(
        EntityState state,
        DateTime now,
        Action<DateTime> setCreated,
        Action<DateTime> setUpdated)
    {
        if (state == EntityState.Added)
        {
            setCreated(now);
        }

        if (state == EntityState.Added || state == EntityState.Modified)
        {
            setUpdated(now);
        }
    }
}
