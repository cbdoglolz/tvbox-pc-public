namespace TVBoxForWindows.Engine;

public static class CatalogYears
{
    public static CatalogChoice[] Choices(int currentYear) => new[] { new CatalogChoice("", "年份：全部") }
        .Concat(Enumerable.Range(1990, Math.Max(1, currentYear - 1989)).Reverse().Select(y => new CatalogChoice("y" + y, y.ToString())))
        .Concat(Enumerable.Range(195, Math.Max(1, currentYear / 10 - 194)).Reverse().Select(d => new CatalogChoice("d" + d * 10, d * 10 + "年代")))
        .Concat(new[] { new CatalogChoice("older", "1950年以前"), new CatalogChoice("custom", "自定义年份…") }).ToArray();
    public static (int? From, int? To) Range(string key, int currentYear)
    {
        if (key.StartsWith('y') && int.TryParse(key[1..], out var year)) return (year, year);
        if (key.StartsWith('d') && int.TryParse(key[1..], out var decade)) return (decade, Math.Min(decade + 9, currentYear));
        return key == "older" ? (1800, 1949) : (null, null);
    }
    public static string Key(int? from, int? to, int currentYear)
    {
        if (from == null && to == null) return "";
        if (from == to && from >= 1990 && from <= currentYear) return "y" + from;
        if (from is >= 1950 && from % 10 == 0 && to == Math.Min(from.Value + 9, currentYear)) return "d" + from;
        return from == 1800 && to == 1949 ? "older" : "custom";
    }
}
