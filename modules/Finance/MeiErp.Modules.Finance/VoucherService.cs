using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

/// <summary>
/// The one way anything reaches the general ledger.
///
/// No module writes financial state directly - they all end up here. That is
/// the guarantee that the books balance, and it must survive every future
/// change to this codebase.
/// </summary>
public interface IVoucherService
{
    Task<IReadOnlyList<Voucher>> ListAsync(VoucherFilter filter, CancellationToken ct = default);
    Task<Voucher?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Saves a draft. Drafts are not in the books and may be edited freely.</summary>
    Task<Result<Voucher>> SaveDraftAsync(VoucherInput input, CancellationToken ct = default);

    /// <summary>Puts a draft into the books. From here it is immutable.</summary>
    Task<Result<Voucher>> PostAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Posts a voucher on behalf of another module. The only entry point other
    /// modules use, and it posts immediately - a system voucher has no draft
    /// stage because there is no human to review it.
    /// </summary>
    Task<Result<Voucher>> PostSystemVoucherAsync(
        SystemVoucher voucher, CancellationToken ct = default);

    /// <summary>
    /// Reverses a posted voucher with an equal and opposite entry.
    ///
    /// The original is never touched. A correction that edits history makes
    /// every previously printed report a lie.
    /// </summary>
    Task<Result<Voucher>> ReverseAsync(int id, string reason, CancellationToken ct = default);

    Task<Result> DeleteDraftAsync(int id, CancellationToken ct = default);

    /// <summary>Copies a posted voucher into a new draft, so it can be fixed and reposted.</summary>
    Task<Result<Voucher>> DuplicateAsDraftAsync(int id, CancellationToken ct = default);
}

public sealed record VoucherFilter(
    DateOnly? From = null, DateOnly? To = null,
    VoucherType? Type = null, VoucherStatus? Status = null,
    int? AccountId = null, string? Search = null, int Take = 200);

public sealed record VoucherInput(
    int? Id, VoucherType Type, DateOnly Date, string Narration,
    IReadOnlyList<VoucherLineInput> Lines);

public sealed record VoucherLineInput(
    int AccountId, decimal Debit, decimal Credit,
    string? Narration = null, string? PersonId = null, string? PersonName = null);

/// <param name="Module">Which module raised it, so the entry can be traced back.</param>
public sealed record SystemVoucher(
    VoucherType Type, DateOnly Date, string Narration,
    IReadOnlyList<VoucherLineInput> Lines,
    string Module, string DocumentType, int DocumentId, string DocumentReference);

public sealed class VoucherService(
    FinanceDbContext db, IClock clock, ICurrentUser currentUser) : IVoucherService
{
    public async Task<IReadOnlyList<Voucher>> ListAsync(
        VoucherFilter filter, CancellationToken ct = default)
    {
        var query = db.Vouchers.AsNoTracking().Include(v => v.Lines).AsQueryable();

        if (filter.From is not null) query = query.Where(v => v.Date >= filter.From);
        if (filter.To is not null) query = query.Where(v => v.Date <= filter.To);
        if (filter.Type is not null) query = query.Where(v => v.Type == filter.Type);
        if (filter.Status is not null) query = query.Where(v => v.Status == filter.Status);

        if (filter.AccountId is not null)
            query = query.Where(v => v.Lines.Any(l => l.AccountId == filter.AccountId));

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(v =>
                EF.Functions.ILike(v.Number, pattern) ||
                EF.Functions.ILike(v.Narration, pattern));
        }

        return await query
            .OrderByDescending(v => v.Date)
            .ThenByDescending(v => v.Id)
            .Take(filter.Take)
            .ToListAsync(ct);
    }

    public Task<Voucher?> GetAsync(int id, CancellationToken ct = default) =>
        db.Vouchers.Include(v => v.Lines).ThenInclude(l => l.Account)
                   .FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<Result<Voucher>> SaveDraftAsync(
        VoucherInput input, CancellationToken ct = default)
    {
        var validated = await ValidateLinesAsync(input.Date, input.Lines, ct);
        if (validated.Failed) return Result.Fail<Voucher>(validated.Error!, validated.Code);

        Voucher voucher;

        if (input.Id is null or 0)
        {
            voucher = new Voucher
            {
                Number = await NextNumberAsync(input.Type, ct),
                Type = input.Type,
                Status = VoucherStatus.Draft
            };
            db.Vouchers.Add(voucher);
        }
        else
        {
            var existing = await db.Vouchers.Include(v => v.Lines)
                .FirstOrDefaultAsync(v => v.Id == input.Id, ct);

            if (existing is null)
                return Result.Fail<Voucher>("That voucher no longer exists.", "voucher.not-found");

            if (existing.IsPosted)
            {
                // The correction path is reverse, or duplicate-as-draft. Editing
                // a posted entry rewrites history that reports already showed.
                return Result.Fail<Voucher>(
                    "A posted voucher cannot be edited. Reverse it, or duplicate it as a draft and fix that.",
                    "voucher.posted-immutable");
            }

            db.VoucherLines.RemoveRange(existing.Lines);
            existing.Lines.Clear();
            voucher = existing;
        }

        voucher.Date = input.Date;
        voucher.Narration = input.Narration;
        voucher.Lines = [.. validated.Value];

        await db.SaveChangesAsync(ct);
        return Result.Success(voucher);
    }

    public async Task<Result<Voucher>> PostAsync(int id, CancellationToken ct = default)
    {
        var voucher = await db.Vouchers.Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (voucher is null)
            return Result.Fail<Voucher>("That voucher no longer exists.", "voucher.not-found");

        if (voucher.IsPosted)
            return Result.Fail<Voucher>("This is already posted.", "voucher.already-posted");

        if (voucher.Lines.Count < 2)
        {
            return Result.Fail<Voucher>(
                "An entry needs at least two lines - something given and something received.",
                "voucher.too-few-lines");
        }

        if (!voucher.IsBalanced)
        {
            return Result.Fail<Voucher>(
                $"Debits total {voucher.TotalDebit:N2} and credits total {voucher.TotalCredit:N2}. " +
                "They must be equal.",
                "voucher.unbalanced");
        }

        var period = await CheckPeriodOpenAsync(voucher.Date, ct);
        if (period.Failed) return Result.Fail<Voucher>(period.Error!, period.Code);

        voucher.Status = VoucherStatus.Posted;
        voucher.PostedUtc = clock.UtcNow;
        voucher.PostedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
        return Result.Success(voucher);
    }

    public async Task<Result<Voucher>> PostSystemVoucherAsync(
        SystemVoucher voucher, CancellationToken ct = default)
    {
        var validated = await ValidateLinesAsync(voucher.Date, voucher.Lines, ct);
        if (validated.Failed) return Result.Fail<Voucher>(validated.Error!, validated.Code);

        var lines = validated.Value;

        var debit = lines.Sum(l => l.Debit);
        var credit = lines.Sum(l => l.Credit);

        if (debit != credit)
        {
            // A module handing over an unbalanced entry is a bug in that module.
            // Refusing it here is what stops one bad caller corrupting the books.
            return Result.Fail<Voucher>(
                $"{voucher.Module} tried to post an unbalanced entry: " +
                $"debits {debit:N2}, credits {credit:N2}.",
                "voucher.unbalanced");
        }

        var period = await CheckPeriodOpenAsync(voucher.Date, ct);
        if (period.Failed) return Result.Fail<Voucher>(period.Error!, period.Code);

        var entry = new Voucher
        {
            Number = await NextNumberAsync(voucher.Type, ct),
            Type = voucher.Type,
            Date = voucher.Date,
            Narration = voucher.Narration,
            Lines = [.. lines],

            SourceModule = voucher.Module,
            SourceDocumentType = voucher.DocumentType,
            SourceDocumentId = voucher.DocumentId,
            SourceReference = voucher.DocumentReference,

            // System vouchers post immediately: there is no human to review a
            // draft, and a draft nobody posts is money missing from the books.
            Status = VoucherStatus.Posted,
            PostedUtc = clock.UtcNow,
            PostedBy = currentUser.UserId ?? "system"
        };

        db.Vouchers.Add(entry);
        await db.SaveChangesAsync(ct);

        return Result.Success(entry);
    }

    public async Task<Result<Voucher>> ReverseAsync(
        int id, string reason, CancellationToken ct = default)
    {
        var original = await db.Vouchers.Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (original is null)
            return Result.Fail<Voucher>("That voucher no longer exists.", "voucher.not-found");

        // Checked before the posted test on purpose: reversing sets the status
        // to Reversed, so a second attempt would otherwise be told "only a
        // posted voucher can be reversed - delete the draft instead", which is
        // both wrong and confusing for an entry that is very much in the books.
        if (original.ReversedByVoucherId is not null)
        {
            return Result.Fail<Voucher>(
                "This has already been reversed. Look for the contra entry against it.",
                "voucher.already-reversed");
        }

        if (!original.IsPosted)
            return Result.Fail<Voucher>("Only a posted voucher can be reversed. Delete the draft instead.", "voucher.not-posted");

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Fail<Voucher>("Say why this is being reversed.", "voucher.no-reason");

        // Reverse into today's open period, not the original's date, which may
        // sit in a year that has since been closed and signed off.
        var reversalDate = clock.Today;
        var period = await CheckPeriodOpenAsync(reversalDate, ct);
        if (period.Failed) return Result.Fail<Voucher>(period.Error!, period.Code);

        var reversal = new Voucher
        {
            Number = await NextNumberAsync(original.Type, ct),
            Type = original.Type,
            Date = reversalDate,
            Narration = $"Reversal of {original.Number}: {reason}",
            Status = VoucherStatus.Posted,
            PostedUtc = clock.UtcNow,
            PostedBy = currentUser.UserId,
            ReversalOfVoucherId = original.Id,
            SourceModule = original.SourceModule,
            SourceDocumentType = original.SourceDocumentType,
            SourceDocumentId = original.SourceDocumentId,

            // Debits become credits and vice versa. The pair nets to nothing,
            // and both remain visible - which is the point.
            Lines = [.. original.Lines.Select(l => new VoucherLine
            {
                AccountId = l.AccountId,
                AccountCode = l.AccountCode,
                AccountName = l.AccountName,
                Debit = l.Credit,
                Credit = l.Debit,
                Narration = l.Narration,
                PersonId = l.PersonId,
                PersonName = l.PersonName
            })]
        };

        db.Vouchers.Add(reversal);
        await db.SaveChangesAsync(ct);

        original.Status = VoucherStatus.Reversed;
        original.ReversedByVoucherId = reversal.Id;
        await db.SaveChangesAsync(ct);

        return Result.Success(reversal);
    }

    public async Task<Result> DeleteDraftAsync(int id, CancellationToken ct = default)
    {
        var voucher = await db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (voucher is null) return Result.Fail("That voucher no longer exists.", "voucher.not-found");

        if (voucher.IsPosted)
            return Result.Fail("A posted voucher cannot be deleted. Reverse it instead.", "voucher.posted-immutable");

        // Soft delete, like everything else - the number stays used so the
        // sequence never appears to have a hole in it.
        db.Vouchers.Remove(voucher);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<Voucher>> DuplicateAsDraftAsync(int id, CancellationToken ct = default)
    {
        var original = await db.Vouchers.Include(v => v.Lines)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (original is null)
            return Result.Fail<Voucher>("That voucher no longer exists.", "voucher.not-found");

        var copy = new Voucher
        {
            Number = await NextNumberAsync(original.Type, ct),
            Type = original.Type,
            Date = clock.Today,
            Narration = original.Narration,
            Status = VoucherStatus.Draft,
            Lines = [.. original.Lines.Select(l => new VoucherLine
            {
                AccountId = l.AccountId,
                AccountCode = l.AccountCode,
                AccountName = l.AccountName,
                Debit = l.Debit,
                Credit = l.Credit,
                Narration = l.Narration,
                PersonId = l.PersonId,
                PersonName = l.PersonName
            })]
        };

        db.Vouchers.Add(copy);
        await db.SaveChangesAsync(ct);
        return Result.Success(copy);
    }

    /// <summary>
    /// Checks the lines make sense and snapshots the account code and name onto
    /// each, so a ledger printed today still reads correctly after a rename.
    /// </summary>
    private async Task<Result<List<VoucherLine>>> ValidateLinesAsync(
        DateOnly date, IReadOnlyList<VoucherLineInput> inputs, CancellationToken ct)
    {
        if (inputs.Count == 0)
            return Result.Fail<List<VoucherLine>>("An entry needs at least one line.", "voucher.no-lines");

        var ids = inputs.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        var lines = new List<VoucherLine>();

        foreach (var input in inputs)
        {
            if (!accounts.TryGetValue(input.AccountId, out var account))
                return Result.Fail<List<VoucherLine>>("One of the lines points at an account that no longer exists.", "voucher.bad-account");

            if (!account.IsPostable)
            {
                // A parent with its own balance double-counts itself against
                // the children beneath it in every report.
                return Result.Fail<List<VoucherLine>>(
                    $"{account.Code} {account.Name} is a heading, not a postable account. " +
                    "Pick one of the accounts underneath it.",
                    "voucher.not-postable");
            }

            if (!account.IsActive)
                return Result.Fail<List<VoucherLine>>($"{account.Code} {account.Name} is no longer in use.", "voucher.inactive-account");

            if (input.Debit < 0 || input.Credit < 0)
                return Result.Fail<List<VoucherLine>>("A line cannot carry a negative amount. Put it on the other side instead.", "voucher.negative");

            if (input.Debit > 0 && input.Credit > 0)
                return Result.Fail<List<VoucherLine>>("A line is either a debit or a credit, never both.", "voucher.both-sides");

            if (input.Debit == 0 && input.Credit == 0)
                continue;   // an empty row from the editor, quietly dropped

            lines.Add(new VoucherLine
            {
                AccountId = account.Id,
                AccountCode = account.Code,
                AccountName = account.Name,
                Debit = input.Debit,
                Credit = input.Credit,
                Narration = input.Narration,
                PersonId = input.PersonId,
                PersonName = input.PersonName
            });
        }

        return lines.Count == 0
            ? Result.Fail<List<VoucherLine>>("Every line is empty.", "voucher.no-lines")
            : Result.Success(lines);
    }

    /// <summary>
    /// Refuses to post into a closed year. Without this, a signed-off trial
    /// balance quietly changes months after it was agreed.
    /// </summary>
    private async Task<Result> CheckPeriodOpenAsync(DateOnly date, CancellationToken ct)
    {
        var closed = await db.FiscalYears
            .Where(y => y.IsClosed && y.StartDate <= date && y.EndDate >= date)
            .Select(y => y.Name)
            .FirstOrDefaultAsync(ct);

        return closed is null
            ? Result.Success()
            : Result.Fail(
                $"{date:d MMM yyyy} falls in {closed}, which is closed. " +
                "Post it into the current year instead.",
                "voucher.period-closed");
    }

    private async Task<string> NextNumberAsync(VoucherType type, CancellationToken ct)
    {
        var prefix = type switch
        {
            VoucherType.Payment => "PV",
            VoucherType.Receipt => "RV",
            VoucherType.Contra => "CV",
            VoucherType.Closing => "CL",
            _ => "JV"
        };

        var year = clock.Today.Year;
        var stem = $"{prefix}-{year % 100:D2}-";

        var count = await db.Vouchers
            .IgnoreQueryFilters()
            .CountAsync(v => v.Number.StartsWith(stem), ct);

        return stem + (count + 1).ToString().PadLeft(4, '0');
    }
}
