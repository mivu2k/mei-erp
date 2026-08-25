using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Tender;

public enum FileOwnerType { Tender, Project }
public enum PhysicalFileStatus { InRegistry, Issued, Archived, Lost }
public enum FileMovementAction { Opened, Issued, Returned, Transferred, Archived, Reopened, MarkedLost, Found }

public class PhysicalFile : AuditableEntity, IConcurrencyChecked
{
    public uint Version { get; set; }
    public string FileNumber { get; set; } = "";
    public FileOwnerType OwnerType { get; set; }
    public int OwnerId { get; set; }
    public string OwnerReference { get; set; } = "";
    public string OwnerTitle { get; set; } = "";
    public PhysicalFileStatus Status { get; set; }
    public string? HolderUserId { get; set; }
    public string? HolderName { get; set; }
    public string? Location { get; set; }
    public string? VolumeNumber { get; set; }
    public DateOnly OpenedOn { get; set; }
    public DateOnly? ClosedOn { get; set; }
    public string? Remarks { get; set; }
    public List<FileMovement> Movements { get; set; } = [];
    public int? DaysOutOn(DateOnly today) => Status != PhysicalFileStatus.Issued ? null : Movements.Where(x => x.Action is FileMovementAction.Issued or FileMovementAction.Transferred).OrderByDescending(x => x.MovedOn).ThenByDescending(x => x.Id).Select(x => today.DayNumber - x.MovedOn.DayNumber).FirstOrDefault();
}

public class FileMovement : AuditableEntity
{
    public int PhysicalFileId { get; set; }
    public PhysicalFile? PhysicalFile { get; set; }
    public FileMovementAction Action { get; set; }
    public DateOnly MovedOn { get; set; }
    public string? FromHolderName { get; set; }
    public string? FromLocation { get; set; }
    public string? ToHolderUserId { get; set; }
    public string? ToHolderName { get; set; }
    public string? ToLocation { get; set; }
    public string? Purpose { get; set; }
    public DateOnly? DueBack { get; set; }
    public string? Remarks { get; set; }
    public string RecordedById { get; set; } = "";
    public string RecordedByName { get; set; } = "";
}

public sealed record FileFilter(string? Search = null, PhysicalFileStatus? Status = null, FileOwnerType? OwnerType = null, bool OverdueOnly = false);
public sealed record FileMoveInput(string? HolderUserId = null, string? HolderName = null, string? Location = null, string? Purpose = null, DateOnly? DueBack = null, string? Remarks = null);

public static class FileRegistryRules
{
    public static Result Validate(PhysicalFile f, FileMovementAction a, FileMoveInput i)
    {
        if (a == FileMovementAction.Issued && f.Status == PhysicalFileStatus.Issued) return Result.Fail($"Already out with {f.HolderName}.", "file.already-out");
        if (a == FileMovementAction.Issued && f.Status == PhysicalFileStatus.Lost) return Result.Fail("Mark the file found before issuing it.", "file.lost");
        // Without this an archived file could be issued straight back out,
        // quietly reopening something that was closed without ever passing
        // through Reopened - so the register shows it live and the closing
        // date still stands.
        if (a == FileMovementAction.Issued && f.Status == PhysicalFileStatus.Archived) return Result.Fail("Reopen the file before issuing it.", "file.archived");
        if (a == FileMovementAction.MarkedLost && f.Status == PhysicalFileStatus.Lost) return Result.Fail("This file is already marked lost.", "file.already-lost");
        if (a is FileMovementAction.Issued or FileMovementAction.Transferred && string.IsNullOrWhiteSpace(i.HolderName)) return Result.Fail("Say who is taking the file.", "file.no-holder");
        if (a == FileMovementAction.Transferred && f.Status != PhysicalFileStatus.Issued) return Result.Fail("Only a file that is out can be handed on.", "file.not-out");
        if (a == FileMovementAction.Returned && f.Status != PhysicalFileStatus.Issued) return Result.Fail("The file is not currently out.", "file.not-out");
        if (a == FileMovementAction.Archived && f.Status == PhysicalFileStatus.Issued) return Result.Fail("Return the file before archiving it.", "file.still-out");
        if (a == FileMovementAction.Reopened && f.Status != PhysicalFileStatus.Archived) return Result.Fail("Only an archived file can be reopened.", "file.not-archived");
        if (a == FileMovementAction.Found && f.Status != PhysicalFileStatus.Lost) return Result.Fail("The file is not marked lost.", "file.not-lost");
        return Result.Success();
    }
}

public interface IFileRegistryService
{
    Task<Result<PhysicalFile>> EnsureAsync(FileOwnerType type, int ownerId, CancellationToken ct = default);
    Task<PhysicalFile?> GetAsync(int id, CancellationToken ct = default);
    Task<PhysicalFile?> GetByNumberAsync(string number, CancellationToken ct = default);
    Task<IReadOnlyList<PhysicalFile>> ListAsync(FileFilter? filter = null, CancellationToken ct = default);
    Task<Result<PhysicalFile>> MoveAsync(int id, FileMovementAction action, FileMoveInput input, CancellationToken ct = default);
    Task<Result<PhysicalFile>> UpdateDetailsAsync(int id, string? location, string? volume, string? remarks, CancellationToken ct = default);
}

public sealed class FileRegistryService(TenderDbContext db, IClock clock, ICurrentUser user) : IFileRegistryService
{
    public async Task<Result<PhysicalFile>> EnsureAsync(FileOwnerType type, int ownerId, CancellationToken ct = default)
    {
        var existing = await db.PhysicalFiles.FirstOrDefaultAsync(x => x.OwnerType == type && x.OwnerId == ownerId, ct);
        string reference, title;
        if (type == FileOwnerType.Tender)
        {
            var owner = await db.Tenders.FindAsync([ownerId], ct); if (owner is null) return Result.Fail<PhysicalFile>("Tender not found.", "file.owner-not-found");
            reference = owner.Reference; title = owner.Title;
        }
        else
        {
            var owner = await db.Projects.FindAsync([ownerId], ct); if (owner is null) return Result.Fail<PhysicalFile>("Project not found.", "file.owner-not-found");
            reference = owner.Code; title = owner.Name;
        }
        if (existing is not null) { existing.OwnerReference = reference; existing.OwnerTitle = title; await db.SaveChangesAsync(ct); return Result.Success(existing); }
        var count = await db.PhysicalFiles.IgnoreQueryFilters().CountAsync(ct);
        var file = new PhysicalFile { FileNumber = $"FILE-{clock.Today.Year % 100:D2}-{count + 1:D4}", OwnerType = type, OwnerId = ownerId, OwnerReference = reference, OwnerTitle = title, OpenedOn = clock.Today };
        file.Movements.Add(NewMovement(file, FileMovementAction.Opened, new FileMoveInput(Remarks: "File opened.")));
        db.Add(file); await db.SaveChangesAsync(ct); return Result.Success(file);
    }

    public Task<PhysicalFile?> GetAsync(int id, CancellationToken ct = default) => db.PhysicalFiles.Include(x => x.Movements.OrderByDescending(m => m.MovedOn).ThenByDescending(m => m.Id)).FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<PhysicalFile?> GetByNumberAsync(string number, CancellationToken ct = default) { var n = number.Trim().ToUpperInvariant(); return db.PhysicalFiles.Include(x => x.Movements).FirstOrDefaultAsync(x => x.FileNumber == n, ct); }

    public async Task<IReadOnlyList<PhysicalFile>> ListAsync(FileFilter? filter = null, CancellationToken ct = default)
    {
        filter ??= new(); var q = db.PhysicalFiles.AsNoTracking().Include(x => x.Movements).AsSplitQuery().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var s = filter.Search.Trim(); q = q.Where(x => x.FileNumber.Contains(s) || x.OwnerReference.Contains(s) || x.OwnerTitle.Contains(s) || (x.HolderName != null && x.HolderName.Contains(s)) || (x.Location != null && x.Location.Contains(s))); }
        if (filter.Status is { } status) q = q.Where(x => x.Status == status); if (filter.OwnerType is { } type) q = q.Where(x => x.OwnerType == type);
        var rows = await q.OrderByDescending(x => x.Id).ToListAsync(ct);
        return filter.OverdueOnly ? rows.Where(IsOverdue).ToList() : rows;
    }

    public async Task<Result<PhysicalFile>> MoveAsync(int id, FileMovementAction action, FileMoveInput input, CancellationToken ct = default)
    {
        var file = await db.PhysicalFiles.FirstOrDefaultAsync(x => x.Id == id, ct); if (file is null) return Result.Fail<PhysicalFile>("File not found.", "file.not-found");
        var valid = FileRegistryRules.Validate(file, action, input); if (valid.Failed) return Result.Fail<PhysicalFile>(valid.Error!, valid.Code);
        var movement = NewMovement(file, action, input);
        switch (action)
        {
            case FileMovementAction.Issued: case FileMovementAction.Transferred: file.Status = PhysicalFileStatus.Issued; file.HolderUserId = input.HolderUserId; file.HolderName = input.HolderName!.Trim(); movement.ToHolderUserId = input.HolderUserId; movement.ToHolderName = file.HolderName; movement.Purpose = input.Purpose; movement.DueBack = input.DueBack; break;
            case FileMovementAction.Returned: case FileMovementAction.Found: file.Status = PhysicalFileStatus.InRegistry; ClearHolder(file); SetLocation(file, movement, input.Location); break;
            case FileMovementAction.Archived: file.Status = PhysicalFileStatus.Archived; file.ClosedOn = clock.Today; ClearHolder(file); SetLocation(file, movement, input.Location); break;
            case FileMovementAction.Reopened: file.Status = PhysicalFileStatus.InRegistry; file.ClosedOn = null; break;
            case FileMovementAction.MarkedLost: file.Status = PhysicalFileStatus.Lost; break;
        }
        db.Add(movement); await db.SaveChangesAsync(ct); return Result.Success(file);
    }

    public async Task<Result<PhysicalFile>> UpdateDetailsAsync(int id, string? location, string? volume, string? remarks, CancellationToken ct = default)
    { var file = await db.PhysicalFiles.FindAsync([id], ct); if (file is null) return Result.Fail<PhysicalFile>("File not found.", "file.not-found"); file.Location = location; file.VolumeNumber = volume; file.Remarks = remarks; await db.SaveChangesAsync(ct); return Result.Success(file); }

    private FileMovement NewMovement(PhysicalFile f, FileMovementAction a, FileMoveInput i) => new() { PhysicalFileId = f.Id, Action = a, MovedOn = clock.Today, FromHolderName = f.HolderName, FromLocation = f.Location, Remarks = i.Remarks, RecordedById = user.UserId ?? "", RecordedByName = user.Name ?? "System" };
    private bool IsOverdue(PhysicalFile f) => f.Status == PhysicalFileStatus.Issued && f.Movements.Where(x => x.Action is FileMovementAction.Issued or FileMovementAction.Transferred).OrderByDescending(x => x.MovedOn).ThenByDescending(x => x.Id).FirstOrDefault()?.DueBack < clock.Today;
    private static void ClearHolder(PhysicalFile f) { f.HolderUserId = null; f.HolderName = null; }
    private static void SetLocation(PhysicalFile f, FileMovement m, string? location) { m.ToLocation = location ?? f.Location; if (!string.IsNullOrWhiteSpace(location)) f.Location = location.Trim(); }
}
