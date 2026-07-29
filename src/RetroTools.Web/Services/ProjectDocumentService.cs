using Microsoft.EntityFrameworkCore;
using RetroTools.Core.Model;
using RetroTools.Core.Palettes;
using RetroTools.Core.Platforms;
using RetroTools.Core.Serialization;
using RetroTools.Data;
using RetroTools.Data.Entities;

namespace RetroTools.Web.Services;

/// <summary>Μεταφράζει ανάμεσα σε αποθηκευμένο project και μεταφέρσιμο έγγραφο JSON.</summary>
public sealed class ProjectDocumentService
{
    private readonly RetroToolsDbContext _context;

    public ProjectDocumentService(RetroToolsDbContext context)
    {
        _context = context;
    }

    // --- Εξαγωγή -------------------------------------------------------------

    /// <summary>
    /// Επιστρέφει <c>null</c> αν το project δεν υπάρχει ή δεν είναι ορατό στον χρήστη.
    /// </summary>
    public async Task<ProjectDocument?> ExportAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var project = await _context.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            .ConfigureAwait(false);

        if (project == null)
        {
            return null;
        }

        var mode = PlatformCatalog.GetMode(project.ModeCode);

        var document = new ProjectDocument
        {
            Generator = "RetroTools Sprite Studio",
            Name = project.Name,
            Description = project.Description,
            PlatformCode = project.PlatformCode,
            ModeCode = project.ModeCode,
            PaletteProfileId = project.PaletteProfileId,
        };

        var palette = await _context.Palettes
            .AsNoTracking()
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);

        var slots = DefaultPalettes.For(mode).ToArray();

        if (palette != null)
        {
            foreach (var entry in palette.Entries.Where(e => e.SlotIndex >= 0 && e.SlotIndex < slots.Length))
            {
                slots[entry.SlotIndex] = entry.HardwareColorIndex;
            }
        }

        for (var slot = 0; slot < slots.Length; slot++)
        {
            document.Palette.Add(new PaletteSlotDocument { Slot = slot, Color = slots[slot] });
        }

        var groups = await _context.SpriteGroups
            .AsNoTracking()
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Τα κλειδιά της βάσης δεν φεύγουν από το σύστημα: το έγγραφο χρησιμοποιεί
        // δικούς του, διαδοχικούς αριθμούς.
        var groupIds = new Dictionary<long, int>();

        for (var i = 0; i < groups.Count; i++)
        {
            groupIds[groups[i].Id] = i + 1;

            document.Groups.Add(new SpriteGroupDocument
            {
                Id = i + 1,
                Name = groups[i].Name,
                SortOrder = groups[i].SortOrder,
            });
        }

        var sprites = await _context.Sprites
            .AsNoTracking()
            .Include(s => s.Frames)
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var spriteIds = new Dictionary<long, int>();

        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            spriteIds[sprite.Id] = i + 1;

            var spriteDocument = new SpriteDocument
            {
                Id = i + 1,
                GroupId = sprite.GroupId.HasValue && groupIds.TryGetValue(sprite.GroupId.Value, out var localGroup)
                    ? localGroup
                    : null,
                Name = sprite.Name,
                Width = sprite.WidthPx,
                Height = sprite.HeightPx,
                HasMask = sprite.HasMask,
                Meta = sprite.MetaJson,
                SortOrder = sprite.SortOrder,
            };

            foreach (var frame in sprite.Frames.OrderBy(f => f.FrameIndex))
            {
                spriteDocument.Frames.Add(new SpriteFrameDocument
                {
                    Index = frame.FrameIndex,
                    DurationMs = frame.DurationMs,
                    Pixels = Convert.ToBase64String(RsprContainer.Read(frame.PixelData).ToArray()),
                    Attributes = frame.AttributeData == null ? null : Convert.ToBase64String(frame.AttributeData),
                    Mask = frame.MaskData == null
                        ? null
                        : Convert.ToBase64String(RsprContainer.Read(frame.MaskData).ToArray()),
                });
            }

            document.Sprites.Add(spriteDocument);
        }

        var maps = await _context.SpriteMaps
            .AsNoTracking()
            .Include(m => m.Cells)
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var map in maps)
        {
            var mapDocument = new SpriteMapDocument
            {
                Name = map.Name,
                Columns = map.Columns,
                Rows = map.Rows,
                CellWidth = map.CellWidthPx,
                CellHeight = map.CellHeightPx,
            };

            foreach (var cell in map.Cells.Where(c => c.SpriteId.HasValue)
                         .OrderBy(c => c.Row).ThenBy(c => c.Column))
            {
                if (!spriteIds.TryGetValue(cell.SpriteId!.Value, out var localSprite))
                {
                    // Το sprite διαγράφηκε· το κελί δεν έχει νόημα να ταξιδέψει.
                    continue;
                }

                mapDocument.Cells.Add(new SpriteMapCellDocument
                {
                    Column = cell.Column,
                    Row = cell.Row,
                    SpriteId = localSprite,
                    FrameIndex = cell.FrameIndex,
                    FlipHorizontal = cell.Flags.HasFlag(SpriteMapCellFlags.FlipHorizontal),
                    FlipVertical = cell.Flags.HasFlag(SpriteMapCellFlags.FlipVertical),
                });
            }

            document.SpriteMaps.Add(mapDocument);
        }

        return document;
    }

    // --- Εισαγωγή ------------------------------------------------------------

    /// <summary>
    /// Δημιουργεί <b>νέο</b> project για τον συγκεκριμένο χρήστη.
    /// </summary>
    /// <remarks>
    /// Η εισαγωγή δεν αντικαθιστά ποτέ υπάρχον project και δεν εμπιστεύεται καμία
    /// πληροφορία ιδιοκτησίας από το αρχείο: ο ιδιοκτήτης είναι πάντα αυτός που
    /// ανεβάζει. Έτσι ένα αρχείο δεν μπορεί ούτε να σβήσει δουλειά ούτε να δείξει
    /// σε δεδομένα άλλου.
    /// </remarks>
    public async Task<Project> ImportAsync(
        ProjectDocument document,
        Guid ownerId,
        string? nameOverride = null,
        CancellationToken cancellationToken = default)
    {
        var errors = ProjectDocumentValidator.Validate(document);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Το έγγραφο δεν είναι έγκυρο: " + string.Join(" ", errors));
        }

        var mode = PlatformCatalog.GetMode(document.ModeCode);
        var platform = PlatformCatalog.Get(mode.Platform);

        var project = new Project
        {
            OwnerId = ownerId,
            Name = string.IsNullOrWhiteSpace(nameOverride) ? document.Name.Trim() : nameOverride.Trim(),
            Description = document.Description,
            PlatformCode = platform.Code,
            ModeCode = mode.Code,
            PaletteProfileId = document.PaletteProfileId ?? platform.Palette.DefaultProfile.Id,
            Visibility = ProjectVisibility.Private,
        };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await ImportPaletteAsync(document, project, mode, cancellationToken).ConfigureAwait(false);

        var groupIds = await ImportGroupsAsync(document, project, cancellationToken).ConfigureAwait(false);
        var spriteIds = await ImportSpritesAsync(document, project, groupIds, cancellationToken).ConfigureAwait(false);

        await ImportSpriteMapsAsync(document, project, spriteIds, cancellationToken).ConfigureAwait(false);

        return project;
    }

    private async Task ImportPaletteAsync(
        ProjectDocument document,
        Project project,
        GraphicsMode mode,
        CancellationToken cancellationToken)
    {
        var slots = DefaultPalettes.For(mode).ToArray();

        foreach (var entry in document.Palette.Where(e => e.Slot >= 0 && e.Slot < slots.Length))
        {
            slots[entry.Slot] = entry.Color;
        }

        var palette = new Palette { ProjectId = project.Id, Name = "Κύρια" };

        for (var slot = 0; slot < slots.Length; slot++)
        {
            palette.Entries.Add(new PaletteEntry
            {
                SlotIndex = slot,
                HardwareColorIndex = slots[slot],
                Role = (int)mode.PixelSlots[slot].Role,
            });
        }

        _context.Palettes.Add(palette);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<int, long>> ImportGroupsAsync(
        ProjectDocument document,
        Project project,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<int, long>();

        foreach (var group in document.Groups)
        {
            var entity = new SpriteGroup
            {
                ProjectId = project.Id,
                Name = group.Name,
                SortOrder = group.SortOrder,
            };

            _context.SpriteGroups.Add(entity);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            map[group.Id] = entity.Id;
        }

        return map;
    }

    private async Task<Dictionary<int, long>> ImportSpritesAsync(
        ProjectDocument document,
        Project project,
        IReadOnlyDictionary<int, long> groupIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<int, long>();

        foreach (var sprite in document.Sprites)
        {
            var entity = new Sprite
            {
                ProjectId = project.Id,
                GroupId = sprite.GroupId.HasValue && groupIds.TryGetValue(sprite.GroupId.Value, out var groupId)
                    ? groupId
                    : null,
                Name = sprite.Name,
                WidthPx = sprite.Width,
                HeightPx = sprite.Height,
                HasMask = sprite.HasMask,
                MetaJson = sprite.Meta,
                SortOrder = sprite.SortOrder,
            };

            foreach (var frame in sprite.Frames.OrderBy(f => f.Index))
            {
                var pixels = Convert.FromBase64String(frame.Pixels);

                entity.Frames.Add(new SpriteFrame
                {
                    FrameIndex = frame.Index,
                    DurationMs = frame.DurationMs,
                    PixelData = RsprContainer.Write(FrameBuffer.FromPixels(sprite.Width, sprite.Height, pixels)),
                    AttributeData = string.IsNullOrEmpty(frame.Attributes)
                        ? null
                        : Convert.FromBase64String(frame.Attributes),
                    MaskData = string.IsNullOrEmpty(frame.Mask)
                        ? null
                        : RsprContainer.Write(FrameBuffer.FromPixels(
                            sprite.Width,
                            sprite.Height,
                            Convert.FromBase64String(frame.Mask))),
                });
            }

            _context.Sprites.Add(entity);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            map[sprite.Id] = entity.Id;
        }

        return map;
    }

    private async Task ImportSpriteMapsAsync(
        ProjectDocument document,
        Project project,
        IReadOnlyDictionary<int, long> spriteIds,
        CancellationToken cancellationToken)
    {
        foreach (var map in document.SpriteMaps)
        {
            var entity = new SpriteMap
            {
                ProjectId = project.Id,
                Name = map.Name,
                Columns = map.Columns,
                Rows = map.Rows,
                CellWidthPx = map.CellWidth > 0 ? map.CellWidth : 16,
                CellHeightPx = map.CellHeight > 0 ? map.CellHeight : 16,
            };

            _context.SpriteMaps.Add(entity);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var cell in map.Cells)
            {
                if (!spriteIds.TryGetValue(cell.SpriteId, out var spriteId))
                {
                    continue;
                }

                var flags = SpriteMapCellFlags.None;

                if (cell.FlipHorizontal)
                {
                    flags |= SpriteMapCellFlags.FlipHorizontal;
                }

                if (cell.FlipVertical)
                {
                    flags |= SpriteMapCellFlags.FlipVertical;
                }

                _context.SpriteMapCells.Add(new SpriteMapCell
                {
                    SpriteMapId = entity.Id,
                    Column = cell.Column,
                    Row = cell.Row,
                    SpriteId = spriteId,
                    FrameIndex = cell.FrameIndex,
                    Flags = flags,
                });
            }

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
