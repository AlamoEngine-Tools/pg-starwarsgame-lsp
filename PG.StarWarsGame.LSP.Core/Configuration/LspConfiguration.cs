namespace PG.StarWarsGame.LSP.Core.Configuration;

public record LspConfiguration
{
    public string? GamePath { get; init; }
    public IReadOnlyList<string> ModPaths { get; init; } = [];
    public string Locale { get; init; } = "en";
    public SchemaSourceConfig SchemaSource { get; init; } = new();
    public BaselineSourceConfig BaselineSource { get; init; } = new();
}

public record SchemaSourceConfig
{
    public SchemaSourceType Type { get; init; } = SchemaSourceType.Http;

    public string Url { get; init; } =
        "https://raw.githubusercontent.com/AlamoEngine-Tools/eaw-schema/refs/heads/main/eaw/";

    public string? LocalPath { get; init; }
}

public enum SchemaSourceType
{
    Http,
    Local
}

public record BaselineSourceConfig
{
    public BaselineSourceType Type { get; init; } = BaselineSourceType.Http;

    public string EawUrl { get; init; } =
        "https://github.com/AlamoEngine-Tools/pg-eaw-baselines/releases/latest/download/eaw-baseline.json";

    public string FocUrl { get; init; } =
        "https://github.com/AlamoEngine-Tools/pg-eaw-baselines/releases/latest/download/foc-baseline.json";

    public string? LocalPath { get; init; }
}

public enum BaselineSourceType
{
    Http,
    Local,
    None
}