namespace MeiErp.LegacyImport;

public static class TenderMapping
{
    public static int Status(int legacy) => legacy switch
    {
        0 => 0, 1 => 1, 2 => 2, 3 => 7, 4 => 4, 5 => 5, 6 => 8, 7 => 6,
        _ => throw new InvalidDataException($"Unknown legacy tender status {legacy}.")
    };

    public static int GuaranteeKind(int legacy) => legacy switch
    {
        0 => 0, 1 => 1, 2 => 2, 3 => 5, 4 => 3, 5 => 4, 6 => 6,
        _ => throw new InvalidDataException($"Unknown legacy guarantee type {legacy}.")
    };

    public static int Bounded(int value, int min, int max, string name) =>
        value >= min && value <= max ? value : throw new InvalidDataException($"Unknown {name} {value}.");

    public static decimal NonNegative(decimal value, string name) =>
        value >= 0 ? value : throw new InvalidDataException($"Negative {name}.");
}
