namespace MeiErp.LegacyImport;

public static class LedgerMapping
{
    public static int Nature(int value)=>Range(value,0,1,"ledger nature");
    public static int Status(int value)=>Range(value,0,2,"ledger status");
    public static int Direction(int value)=>Range(value,0,1,"entry direction");
    public static int Kind(int value)=>Range(value,0,1,"entry kind");
    public static int Method(int value)=>Range(value,0,5,"payment method");

    public static void PositiveAmount(decimal value,int entryId)
    {
        if(value<=0)throw new InvalidDataException($"Entry {entryId} amount must be greater than zero.");
    }

    public static IReadOnlyList<string> ValidateHierarchy(
        IReadOnlyDictionary<int,int?> parents,string entityName)
    {
        var errors=new List<string>();
        foreach(var id in parents.Keys)
        {
            var seen=new HashSet<int>();var current=(int?)id;
            while(current is not null)
            {
                if(!seen.Add(current.Value)){errors.Add($"{entityName} hierarchy contains a cycle at {id}.");break;}
                var referenced=current.Value;
                if(!parents.TryGetValue(referenced,out current)){errors.Add($"{entityName} {id} references missing parent {referenced}.");break;}
            }
        }
        return errors.Distinct().ToList();
    }

    private static int Range(int value,int min,int max,string name)=>
        value>=min&&value<=max?value:throw new InvalidDataException($"Unknown legacy {name} {value}.");
}
