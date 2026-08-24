using MeiErp.Modules.Tender;
using Xunit;

namespace MeiErp.Modules.Tender.Tests;

public sealed class TenderTaskRulesTests
{
    [Fact]
    public void A_tender_task_requires_a_title()
    {
        var result = TenderTaskRules.Validate(new TenderTask());

        Assert.True(result.Failed);
        Assert.Equal("task.no-title", result.Code);
    }

    [Fact]
    public void Tender_task_progress_must_be_bounded()
    {
        var result = TenderTaskRules.Validate(new TenderTask { Title = "Review bid", PercentComplete = 101 });

        Assert.True(result.Failed);
        Assert.Equal("task.bad-progress", result.Code);
    }

    [Fact]
    public void Tender_task_due_date_cannot_precede_start_date()
    {
        var result = TenderTaskRules.Validate(new TenderTask
        {
            Title = "Review bid",
            StartDate = new DateOnly(2026, 8, 20),
            DueDate = new DateOnly(2026, 8, 19)
        });

        Assert.True(result.Failed);
        Assert.Equal("task.bad-dates", result.Code);
    }

    [Fact]
    public void A_valid_tender_task_passes_validation()
    {
        var result = TenderTaskRules.Validate(new TenderTask
        {
            Title = "Submit clarification",
            PercentComplete = 40,
            StartDate = new DateOnly(2026, 8, 20),
            DueDate = new DateOnly(2026, 8, 25)
        });

        Assert.False(result.Failed);
    }
}
