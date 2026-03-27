namespace TunnelBackend.Infrastructure.Storage;

public interface IFileStorage
{
    Task<string> SaveAsync(Stream stream, string originalFileName, CancellationToken ct);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct);
}
