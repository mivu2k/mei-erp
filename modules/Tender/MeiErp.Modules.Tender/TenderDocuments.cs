using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Tender;

public enum DocumentCategory
{
    TenderNotice = 0,
    TechnicalBid = 1,
    FinancialBid = 2,
    EligibilityProof = 3,
    EmdReceipt = 4,
    Compliance = 5,
    Contract = 6,
    Correspondence = 7,
    Other = 8
}

/// <summary>Metadata for a tender document; the physical file registry remains the paper-folder tracker.</summary>
public sealed class TenderDocument : AuditableEntity
{
    public int TenderRecordId { get; set; }
    public TenderRecord? Tender { get; set; }
    public DocumentCategory Category { get; set; }
    public string Name { get; set; } = "";
    public string? ReferenceNumber { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A bidder observed at financial opening, including our own bid when recorded.</summary>
public sealed class TenderCompetitor : AuditableEntity
{
    public int TenderRecordId { get; set; }
    public TenderRecord? Tender { get; set; }
    public string BidderName { get; set; } = "";
    public decimal QuotedAmount { get; set; }
    public int? Rank { get; set; }
    public bool IsOwnBid { get; set; }
    public string? Remarks { get; set; }
}

public static class TenderParityRules
{
    public static Result ValidateDocument(TenderDocument document) =>
        string.IsNullOrWhiteSpace(document.Name)
            ? Result.Fail("A document needs a name.", "document.no-name")
            : Result.Success();

    public static Result ValidateCompetitor(TenderCompetitor competitor)
    {
        if (string.IsNullOrWhiteSpace(competitor.BidderName))
            return Result.Fail("A bidder needs a name.", "competitor.no-name");
        if (competitor.QuotedAmount < 0)
            return Result.Fail("A quoted amount cannot be negative.", "competitor.bad-amount");
        if (competitor.Rank is < 1)
            return Result.Fail("A bidder rank must be positive.", "competitor.bad-rank");
        return Result.Success();
    }
}
