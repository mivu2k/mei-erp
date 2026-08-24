using MeiErp.Platform.Kernel;
using MeiErp.Platform.Workflow;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Trade;

public sealed record DocumentLineInput(
    int? ItemId, string? ItemCode, string Description, decimal Quantity, decimal UnitPrice);

public sealed record QuotationInput(
    int? Id, TradeDirection Direction, int PartyId, int BookId,
    DateOnly Date, DateOnly? ValidUntil,
    decimal TaxPercent, decimal Discount,
    string? Notes, string? Terms,
    int? JobId, string? JobReference,
    IReadOnlyList<DocumentLineInput> Lines);

public sealed record InvoiceInput(
    int? Id, TradeDirection Direction, int PartyId, int BookId,
    DateOnly Date, DateOnly? DueDate,
    decimal TaxPercent, decimal Discount,
    string? TheirReference, string? Notes,
    int? SalesOrderId, int? PurchaseOrderId,
    IReadOnlyList<DocumentLineInput> Lines);

/// <summary>
/// Quotations and invoices, in both directions.
///
/// Every write goes through here so the draft/submit/approve lifecycle is
/// enforced in one place. The rule that matters: a draft is editable and binds
/// nobody, anything past it is a real document and is corrected by cancelling
/// and reissuing, never by quietly editing what somebody has already been sent.
/// </summary>
public interface ITradeDocumentService
{
    Task<IReadOnlyList<Quotation>> QuotationsAsync(
        TradeDirection direction, DocumentStatus? status = null, CancellationToken ct = default);
    Task<Quotation?> QuotationAsync(int id, CancellationToken ct = default);

    /// <summary>Creates or updates a draft. Refuses to touch anything already issued.</summary>
    Task<Result<Quotation>> SaveQuotationAsync(QuotationInput input, CancellationToken ct = default);

    /// <summary>Sends it for sign-off. The value decides who has to sign.</summary>
    Task<Result<Quotation>> SubmitQuotationAsync(int id, CancellationToken ct = default);

    /// <summary>Records the other party's answer once it has been signed off and sent.</summary>
    Task<Result<Quotation>> SetQuotationOutcomeAsync(int id, bool accepted, string? comment, CancellationToken ct = default);

    Task<Result> DeleteQuotationDraftAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<Invoice>> InvoicesAsync(
        TradeDirection direction, DocumentStatus? status = null, CancellationToken ct = default);
    Task<Invoice?> InvoiceAsync(int id, CancellationToken ct = default);
    Task<Result<Invoice>> SaveInvoiceAsync(InvoiceInput input, CancellationToken ct = default);

    /// <summary>
    /// Commits the invoice. Above the approval threshold it goes for sign-off
    /// first; below it, it posts directly.
    /// </summary>
    Task<Result<Invoice>> PostInvoiceAsync(int id, CancellationToken ct = default);

    Task<Result> DeleteInvoiceDraftAsync(int id, CancellationToken ct = default);

    /// <summary>Builds an unsaved draft invoice from an order, for the editor to open on.</summary>
    Task<Result<InvoiceInput>> DraftInvoiceFromOrderAsync(
        TradeDirection direction, int orderId, CancellationToken ct = default);
}

public sealed class TradeDocumentService(
    TradeDbContext db, IApprovalEngine approvals, IClock clock) : ITradeDocumentService
{
    public const string QuotationDocumentType = "trade.quotation";
    public const string InvoiceDocumentType = "trade.invoice";

    // ---------------------------------------------------------- quotations

    public async Task<IReadOnlyList<Quotation>> QuotationsAsync(
        TradeDirection direction, DocumentStatus? status = null, CancellationToken ct = default)
    {
        var q = db.Quotations.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.Direction == direction);

        if (status is not null) q = q.Where(x => x.Status == status);

        return await q.OrderByDescending(x => x.Id).Take(500).ToListAsync(ct);
    }

    public Task<Quotation?> QuotationAsync(int id, CancellationToken ct = default) =>
        db.Quotations.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Result<Quotation>> SaveQuotationAsync(
        QuotationInput input, CancellationToken ct = default)
    {
        var party = await PartyForAsync(input.PartyId, input.Direction, ct);
        if (party.Failed) return Result.Fail<Quotation>(party.Error!, party.Code);

        if (input.Lines.Count == 0)
            return Result.Fail<Quotation>("A quotation needs at least one line.", "quotation.no-lines");

        if (input.Lines.Any(l => string.IsNullOrWhiteSpace(l.Description)))
            return Result.Fail<Quotation>("Every line needs a description.", "quotation.no-description");

        if (input.Lines.Any(l => l.Quantity <= 0))
            return Result.Fail<Quotation>("Every line needs a quantity greater than nothing.", "quotation.bad-quantity");

        Quotation row;

        if (input.Id is null or 0)
        {
            row = new Quotation
            {
                Number = await NextNumberAsync(
                    input.Direction == TradeDirection.Sales ? "SQ" : "PQ", ct),
                Direction = input.Direction,
                Status = DocumentStatus.Draft
            };
            db.Quotations.Add(row);
        }
        else
        {
            var existing = await QuotationAsync(input.Id.Value, ct);
            if (existing is null) return Result.Fail<Quotation>("That quotation no longer exists.", "quotation.not-found");

            if (!existing.Status.IsEditable())
            {
                // It has been sent, or is under an approver. Editing the prices
                // underneath either of them is how a business ends up honouring
                // a figure nobody agreed to.
                return Result.Fail<Quotation>(
                    "This quotation has been issued and can no longer be edited. Cancel it and raise a new one.",
                    "quotation.not-editable");
            }

            db.QuotationLines.RemoveRange(existing.Lines);
            existing.Lines.Clear();
            row = existing;
        }

        row.PartyId = party.Value.Id;
        row.PartyName = party.Value.Name;
        row.DomainId = input.BookId;
        row.Date = input.Date;
        row.ValidUntil = input.ValidUntil;
        row.TaxPercent = input.TaxPercent;
        row.Discount = input.Discount;
        row.Notes = input.Notes;
        row.Terms = input.Terms;
        row.JobId = input.JobId;
        row.JobReference = input.JobReference;

        foreach (var l in input.Lines)
        {
            row.Lines.Add(new QuotationLine
            {
                ItemId = l.ItemId,
                ItemCode = l.ItemCode,
                Description = l.Description.Trim(),
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice
            });
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(row);
    }

    public async Task<Result<Quotation>> SubmitQuotationAsync(int id, CancellationToken ct = default)
    {
        var row = await QuotationAsync(id, ct);
        if (row is null) return Result.Fail<Quotation>("That quotation no longer exists.", "quotation.not-found");

        if (!row.Status.IsEditable())
            return Result.Fail<Quotation>("This has already been submitted.", "quotation.already-submitted");

        if (row.Lines.Count == 0)
            return Result.Fail<Quotation>("A quotation needs at least one line.", "quotation.no-lines");

        var submitted = await approvals.SubmitAsync(new SubmitApproval(
            ModuleKey: row.Direction == TradeDirection.Sales ? SalesModule.Key : PurchaseModule.Key,
            DocumentType: QuotationDocumentType,
            DocumentId: row.Id,
            DocumentReference: row.Number,
            Summary: $"{row.PartyName} — {row.Lines.Count} " +
                     $"{(row.Lines.Count == 1 ? "line" : "lines")}, {row.Total:N2}",
            DocumentUrl: row.Direction == TradeDirection.Sales
                ? $"/sales/quotations/{row.Id}"
                : $"/purchase/quotations/{row.Id}",

            // The value decides who has to sign: a large quotation needs more
            // signatures than a small one, without any code here deciding that.
            Amount: row.Total,
            Currency: "PKR"), ct);

        if (submitted.Failed) return Result.Fail<Quotation>(submitted.Error!, submitted.Code);

        row.Status = DocumentStatus.PendingApproval;
        row.ApprovalRequestId = submitted.Value.Id;
        row.DecisionComment = null;

        await db.SaveChangesAsync(ct);
        return Result.Success(row);
    }

    public async Task<Result<Quotation>> SetQuotationOutcomeAsync(
        int id, bool accepted, string? comment, CancellationToken ct = default)
    {
        var row = await QuotationAsync(id, ct);
        if (row is null) return Result.Fail<Quotation>("That quotation no longer exists.", "quotation.not-found");

        // Only something that has been signed off and put in front of the other
        // party can have an answer from them.
        if (row.Status is not (DocumentStatus.Approved or DocumentStatus.Sent))
        {
            return Result.Fail<Quotation>(
                "Only an approved quotation can be marked accepted or declined.", "quotation.not-approved");
        }

        row.Status = accepted ? DocumentStatus.Accepted : DocumentStatus.Rejected;
        row.DecisionComment = comment;

        await db.SaveChangesAsync(ct);
        return Result.Success(row);
    }

    public async Task<Result> DeleteQuotationDraftAsync(int id, CancellationToken ct = default)
    {
        var row = await QuotationAsync(id, ct);
        if (row is null) return Result.Success();

        if (!row.Status.IsEditable())
            return Result.Fail("Only a draft can be deleted. Cancel an issued quotation instead.", "quotation.not-draft");

        db.Quotations.Remove(row);       // soft delete, so the number stays resolvable
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ------------------------------------------------------------ invoices

    public async Task<IReadOnlyList<Invoice>> InvoicesAsync(
        TradeDirection direction, DocumentStatus? status = null, CancellationToken ct = default)
    {
        var q = db.Invoices.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.Direction == direction);

        if (status is not null) q = q.Where(x => x.Status == status);

        return await q.OrderByDescending(x => x.Id).Take(500).ToListAsync(ct);
    }

    public Task<Invoice?> InvoiceAsync(int id, CancellationToken ct = default) =>
        db.Invoices.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Result<Invoice>> SaveInvoiceAsync(
        InvoiceInput input, CancellationToken ct = default)
    {
        var party = await PartyForAsync(input.PartyId, input.Direction, ct);
        if (party.Failed) return Result.Fail<Invoice>(party.Error!, party.Code);

        if (input.Lines.Count == 0)
            return Result.Fail<Invoice>("An invoice needs at least one line.", "invoice.no-lines");

        if (input.Lines.Any(l => string.IsNullOrWhiteSpace(l.Description)))
            return Result.Fail<Invoice>("Every line needs a description.", "invoice.no-description");

        Invoice row;

        if (input.Id is null or 0)
        {
            row = new Invoice
            {
                Number = await NextNumberAsync(
                    input.Direction == TradeDirection.Sales ? "SI" : "PI", ct),
                Direction = input.Direction,
                Status = DocumentStatus.Draft
            };
            db.Invoices.Add(row);
        }
        else
        {
            var existing = await InvoiceAsync(input.Id.Value, ct);
            if (existing is null) return Result.Fail<Invoice>("That invoice no longer exists.", "invoice.not-found");

            if (!existing.Status.IsEditable())
            {
                // A posted invoice is in somebody's books. Correcting it means a
                // credit note, not a quiet edit.
                return Result.Fail<Invoice>(
                    "This invoice has been posted and can no longer be edited.", "invoice.not-editable");
            }

            db.InvoiceLines.RemoveRange(existing.Lines);
            existing.Lines.Clear();
            row = existing;
        }

        row.PartyId = party.Value.Id;
        row.PartyName = party.Value.Name;
        row.DomainId = input.BookId;
        row.Date = input.Date;

        // Net terms from the party unless the person typed a date themselves.
        row.DueDate = input.DueDate
            ?? (party.Value.PaymentTermDays is { } days ? input.Date.AddDays(days) : null);

        row.TaxPercent = input.TaxPercent;
        row.Discount = input.Discount;
        row.TheirReference = input.TheirReference;
        row.Notes = input.Notes;
        row.SalesOrderId = input.SalesOrderId;
        row.PurchaseOrderId = input.PurchaseOrderId;

        foreach (var l in input.Lines)
        {
            row.Lines.Add(new InvoiceLine
            {
                ItemId = l.ItemId,
                ItemCode = l.ItemCode,
                Description = l.Description.Trim(),
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice
            });
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(row);
    }

    public async Task<Result<Invoice>> PostInvoiceAsync(int id, CancellationToken ct = default)
    {
        var row = await InvoiceAsync(id, ct);
        if (row is null) return Result.Fail<Invoice>("That invoice no longer exists.", "invoice.not-found");

        if (!row.Status.IsEditable())
            return Result.Fail<Invoice>("This invoice has already been posted.", "invoice.already-posted");

        if (row.Lines.Count == 0)
            return Result.Fail<Invoice>("An invoice needs at least one line.", "invoice.no-lines");

        var submitted = await approvals.SubmitAsync(new SubmitApproval(
            ModuleKey: row.Direction == TradeDirection.Sales ? SalesModule.Key : PurchaseModule.Key,
            DocumentType: InvoiceDocumentType,
            DocumentId: row.Id,
            DocumentReference: row.Number,
            Summary: $"{row.PartyName} — {row.Total:N2}",
            DocumentUrl: row.Direction == TradeDirection.Sales
                ? $"/sales/invoices/{row.Id}"
                : $"/purchase/invoices/{row.Id}",
            Amount: row.Total,
            Currency: "PKR"), ct);

        if (submitted.Failed) return Result.Fail<Invoice>(submitted.Error!, submitted.Code);

        row.Status = DocumentStatus.PendingApproval;
        row.ApprovalRequestId = submitted.Value.Id;
        row.DecisionComment = null;

        await db.SaveChangesAsync(ct);
        return Result.Success(row);
    }

    public async Task<Result> DeleteInvoiceDraftAsync(int id, CancellationToken ct = default)
    {
        var row = await InvoiceAsync(id, ct);
        if (row is null) return Result.Success();

        if (!row.Status.IsEditable())
            return Result.Fail("Only a draft can be deleted. A posted invoice needs a credit note.", "invoice.not-draft");

        db.Invoices.Remove(row);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<InvoiceInput>> DraftInvoiceFromOrderAsync(
        TradeDirection direction, int orderId, CancellationToken ct = default)
    {
        if (direction == TradeDirection.Sales)
        {
            var order = await db.SalesOrders.AsNoTracking().Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == orderId, ct);

            if (order is null) return Result.Fail<InvoiceInput>("That order no longer exists.", "invoice.no-order");

            return Result.Success(new InvoiceInput(
                null, TradeDirection.Sales, order.PartyId, order.DomainId,
                clock.Today, null, 0, 0, null, null, order.Id, null,
                [.. order.Lines.Select(l => new DocumentLineInput(
                    l.ItemId, l.ItemCode, l.ItemName, l.Quantity, l.UnitPrice))]));
        }

        var po = await db.PurchaseOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (po is null) return Result.Fail<InvoiceInput>("That order no longer exists.", "invoice.no-order");

        return Result.Success(new InvoiceInput(
            null, TradeDirection.Purchase, po.PartyId, po.DomainId,
            clock.Today, null, 0, 0, null, null, null, po.Id,
            [.. po.Lines.Select(l => new DocumentLineInput(
                l.ItemId, l.ItemCode, l.ItemName, l.Quantity, l.UnitCost))]));
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// The party, checked against the side the document faces. The one master
    /// holds both, so a sales document raised against a supplier-only record is
    /// a mistake worth catching here rather than at the printer.
    /// </summary>
    private async Task<Result<Party>> PartyForAsync(
        int partyId, TradeDirection direction, CancellationToken ct)
    {
        var party = await db.Parties.FirstOrDefaultAsync(p => p.Id == partyId, ct);
        if (party is null) return Result.Fail<Party>("That party no longer exists.", "document.no-party");

        if (direction == TradeDirection.Sales && !party.IsCustomer)
            return Result.Fail<Party>($"{party.Name} is not marked as a customer.", "document.not-customer");

        if (direction == TradeDirection.Purchase && !party.IsSupplier)
            return Result.Fail<Party>($"{party.Name} is not marked as a supplier.", "document.not-supplier");

        return Result.Success(party);
    }

    private async Task<string> NextNumberAsync(string prefix, CancellationToken ct)
    {
        var stem = $"{prefix}-{clock.Today.Year % 100:D2}-";

        var count = prefix is "SQ" or "PQ"
            ? await db.Quotations.IgnoreQueryFilters().CountAsync(x => x.Number.StartsWith(stem), ct)
            : await db.Invoices.IgnoreQueryFilters().CountAsync(x => x.Number.StartsWith(stem), ct);

        return stem + (count + 1).ToString().PadLeft(4, '0');
    }
}

/// <summary>How a quotation hears that its approval was decided.</summary>
public sealed class QuotationApprovalSink(TradeDbContext db) : IApprovalSink
{
    public string DocumentType => TradeDocumentService.QuotationDocumentType;

    public async Task<Result> OnSettledAsync(
        int documentId, ApprovalStatus status, ApprovalRequest request, CancellationToken ct = default)
    {
        var row = await db.Quotations.FirstOrDefaultAsync(x => x.Id == documentId, ct);
        if (row is null) return Result.Fail("The quotation behind this approval has gone.", "quotation.not-found");

        row.DecisionComment = request.Actions
            .OrderByDescending(a => a.ActedUtc)
            .Select(a => a.Comment)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        row.Status = status switch
        {
            ApprovalStatus.Approved => DocumentStatus.Approved,
            ApprovalStatus.Rejected => DocumentStatus.Rejected,
            ApprovalStatus.Returned => DocumentStatus.Returned,
            ApprovalStatus.Cancelled => DocumentStatus.Cancelled,
            _ => row.Status
        };

        return Result.Success();
    }
}

/// <summary>
/// How an invoice hears that its approval was decided. Approval is what posts
/// it - that is the moment it enters the books.
/// </summary>
public sealed class InvoiceApprovalSink(TradeDbContext db) : IApprovalSink
{
    public string DocumentType => TradeDocumentService.InvoiceDocumentType;

    public async Task<Result> OnSettledAsync(
        int documentId, ApprovalStatus status, ApprovalRequest request, CancellationToken ct = default)
    {
        var row = await db.Invoices.FirstOrDefaultAsync(x => x.Id == documentId, ct);
        if (row is null) return Result.Fail("The invoice behind this approval has gone.", "invoice.not-found");

        row.DecisionComment = request.Actions
            .OrderByDescending(a => a.ActedUtc)
            .Select(a => a.Comment)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        row.Status = status switch
        {
            ApprovalStatus.Approved => DocumentStatus.Posted,
            ApprovalStatus.Rejected => DocumentStatus.Rejected,
            ApprovalStatus.Returned => DocumentStatus.Returned,
            ApprovalStatus.Cancelled => DocumentStatus.Cancelled,
            _ => row.Status
        };

        return Result.Success();
    }
}
