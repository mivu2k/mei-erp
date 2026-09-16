using MeiErp.Platform.Reporting;
using Xunit;

namespace MeiErp.Host.Tests;

public sealed class ReportRendererTests
{
    private static readonly ReportResult Source = new()
    {
        Columns =
        [
            new("name", "Name"),
            new("team", "Team"),
            new("quantity", "Quantity", ReportValueKind.Number),
            new("value", "Value", ReportValueKind.Money)
        ],
        Rows =
        [
            Row("Alpha", "North", 2, 20m),
            Row("Bravo", "South", 7, 70m),
            Row("Charlie", "North", 4, 40m)
        ],
        Totals = [new("quantity", 13m), new("value", 130m)]
    };

    [Fact]
    public void Apply_Searches_Sorts_And_Selects_Columns_Consistently()
    {
        var result = ReportRenderer.Apply(Source, new ReportRequest
        {
            Search = "North",
            SortBy = "quantity",
            SortDescending = true,
            SelectedColumns = ["name", "quantity"]
        });

        Assert.Equal(["name", "quantity"], result.Columns.Select(x => x.Key));
        Assert.Equal(["Charlie", "Alpha"], result.Rows.Select(x => x["name"]));
        Assert.Single(result.Totals);
        Assert.Equal("quantity", result.Totals[0].ColumnKey);
    }

    [Fact]
    public void Group_Calculates_Number_And_Money_Subtotals()
    {
        var groups = ReportRenderer.Group(Source, "team");
        var north = Assert.Single(groups, x => x.Key == "North");

        Assert.Equal(6m, Assert.Single(north.Totals, x => x.ColumnKey == "quantity").Value);
        Assert.Equal(60m, Assert.Single(north.Totals, x => x.ColumnKey == "value").Value);
    }

    private static ReportRow Row(string name, string team, int quantity, decimal value) =>
        new(new Dictionary<string, object?>
        {
            ["name"] = name,
            ["team"] = team,
            ["quantity"] = quantity,
            ["value"] = value
        });
}
