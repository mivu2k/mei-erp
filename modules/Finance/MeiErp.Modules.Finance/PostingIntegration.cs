using System.Text.Json;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Messaging;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Finance;

public sealed record PostingRuleInput(
    int? Id, string EventType, string Name, int DebitAccountId, int CreditAccountId, bool IsActive);

public interface IPostingRuleService
{
    Task<IReadOnlyList<PostingRule>> ListAsync(CancellationToken ct = default);
    Task<Result<PostingRule>> SaveAsync(PostingRuleInput input, CancellationToken ct = default);
}

public sealed class PostingRuleService(FinanceDbContext db) : IPostingRuleService
{
    public async Task<IReadOnlyList<PostingRule>> ListAsync(CancellationToken ct = default) =>
        await db.PostingRules.AsNoTracking().Include(r => r.DebitAccount)
            .Include(r => r.CreditAccount).OrderBy(r => r.Name).ToListAsync(ct);

    public async Task<Result<PostingRule>> SaveAsync(PostingRuleInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.EventType) || string.IsNullOrWhiteSpace(input.Name))
            return Result.Fail<PostingRule>("Event type and rule name are required.", "posting-rule.required");
        if (input.DebitAccountId == input.CreditAccountId)
            return Result.Fail<PostingRule>("Debit and credit accounts must be different.", "posting-rule.same-account");
        var accounts = await db.Accounts.CountAsync(a =>
            a.Id == input.DebitAccountId || a.Id == input.CreditAccountId, ct);
        if (accounts != 2) return Result.Fail<PostingRule>("One of the selected accounts no longer exists.", "posting-rule.account-missing");
        var row = input.Id is null or 0 ? new PostingRule() :
            await db.PostingRules.FirstOrDefaultAsync(r => r.Id == input.Id, ct) ?? new PostingRule();
        if (input.Id is not null and not 0 && row.Id == 0)
            return Result.Fail<PostingRule>("Posting rule not found.", "posting-rule.not-found");
        if (row.Id == 0) db.PostingRules.Add(row);
        row.EventType = input.EventType.Trim(); row.Name = input.Name.Trim();
        row.DebitAccountId = input.DebitAccountId; row.CreditAccountId = input.CreditAccountId;
        row.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return Result.Success(row);
    }
}

public sealed class GoodsReceiptPostingHandler(
    FinanceDbContext db, IVoucherService vouchers) : IIntegrationEventConsumer
{
    public const string Event = "inventory.goods-receipt.posted";
    public string EventType => Event;
    private sealed record Payload(string Number, DateOnly Date, int PartyId, string PartyName, decimal Amount);

    public async Task<Result> HandleAsync(string payload, string? causedByUserId, CancellationToken ct = default)
    {
        var data = JsonSerializer.Deserialize<Payload>(payload);
        if (data is null || data.Amount <= 0) return Result.Fail("Invalid goods receipt payload.", "posting.bad-payload");
        var rule = await db.PostingRules.AsNoTracking().FirstOrDefaultAsync(r =>
            r.EventType == Event && r.IsActive, ct);
        if (rule is null) return Result.Fail("No active Finance posting rule exists for goods receipts.", "posting.no-rule");
        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            VoucherType.Journal, data.Date, $"Goods received from {data.PartyName} — {data.Number}",
            [new(rule.DebitAccountId, data.Amount, 0, data.Number),
             new(rule.CreditAccountId, 0, data.Amount, data.Number, data.PartyId.ToString(), data.PartyName)],
            "inventory", "goods-receipt", 0, data.Number, data.Number), ct);
        return posted.Failed ? Result.Fail(posted.Error!, posted.Code) : Result.Success();
    }
}

public sealed class RepairOrderPostingHandler(
    FinanceDbContext db, IVoucherService vouchers) : IIntegrationEventConsumer
{
    public const string Event = "repair.order.posted";
    public string EventType => Event;
    private sealed record Payload(string Number, DateOnly Date, int CustomerId, string CustomerName, decimal Amount);
    public async Task<Result> HandleAsync(string payload, string? causedByUserId, CancellationToken ct = default)
    {
        var data = JsonSerializer.Deserialize<Payload>(payload);
        if (data is null || data.Amount <= 0) return Result.Fail("Invalid repair order payload.", "posting.bad-payload");
        var rule = await db.PostingRules.AsNoTracking().FirstOrDefaultAsync(r => r.EventType == Event && r.IsActive, ct);
        if (rule is null) return Result.Fail("No active Finance posting rule exists for repair orders.", "posting.no-rule");
        var posted = await vouchers.PostSystemVoucherAsync(new SystemVoucher(
            VoucherType.Journal, data.Date, $"Repair order for {data.CustomerName} — {data.Number}",
            [new(rule.DebitAccountId, data.Amount, 0, data.Number, data.CustomerId.ToString(), data.CustomerName),
             new(rule.CreditAccountId, 0, data.Amount, data.Number)],
            "repair", "repair-order", 0, data.Number, data.Number), ct);
        return posted.Failed ? Result.Fail(posted.Error!, posted.Code) : Result.Success();
    }
}
