namespace PG.StarWarsGame.LSP.Xml;

internal static class DescriptionResolver
{
    private const string NoneMessage =
        "_No description available. Help the community by [contributing one via a PR](https://github.com/AlamoEngine-Tools/pg-eaw-schema)._";

    public static string Resolve(IReadOnlyDictionary<string, string> descriptions, string locale)
    {
        if (descriptions.TryGetValue(locale, out var text))
            return text;

        if (!string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase) &&
            descriptions.TryGetValue("en", out text))
            return text;

        return NoneMessage;
    }
}