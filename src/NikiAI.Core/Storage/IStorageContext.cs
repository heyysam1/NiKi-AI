namespace NikiAI.Core.Storage;

/// <summary>
/// Storage context contract for persistent local data (SQLite).
/// </summary>
public interface IStorageContext
{
    string DatabasePath { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
