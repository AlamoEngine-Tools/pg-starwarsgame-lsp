namespace PG.StarWarsGame.LSP.Core.Configuration;

public interface ILspConfigurationProvider
{
    LspConfiguration Current { get; }

    /// <summary>
    ///     Merges <paramref name="initializationOptions" /> (from the LSP initialize request)
    ///     on top of any values already loaded from the workspace config file.
    /// </summary>
    void LoadFrom(object? initializationOptions);
}