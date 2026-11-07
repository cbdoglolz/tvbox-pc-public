namespace TVBoxForWindows.Engine;

public static class LocalizedSynopsis
{
    public static string Chinese(params string[] candidates) => candidates.FirstOrDefault(s =>
        !string.IsNullOrWhiteSpace(s) && s.Any(c => c >= '\u4e00' && c <= '\u9fff')) ?? "";
}
