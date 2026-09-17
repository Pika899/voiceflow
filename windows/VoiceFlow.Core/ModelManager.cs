using System.Security.Cryptography;

namespace VoiceFlow.Core;

public sealed class ChecksumMismatchException(string expected, string actual)
    : Exception($"Checksum mismatch: expected {expected}, got {actual}")
{
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
}

public sealed class ModelDownloadException(string message, Exception? inner) : Exception(message, inner);

public sealed class ModelManager(string modelsDirectory, HttpMessageHandler? handler = null)
{
    private const int BufferSize = 1 << 20; // 1 MiB, matches the Swift streaming chunk size.

    private readonly HttpClient httpClient = handler is null ? new HttpClient() : new HttpClient(handler);

    public string LocalPath(ModelInfo model) => Path.Combine(modelsDirectory, model.FileName);

    public bool IsModelPresentAndValid(ModelInfo model)
    {
        var path = LocalPath(model);
        if (!File.Exists(path))
        {
            return false;
        }

        return Sha256OfFile(path) == model.Sha256;
    }

    public static string Sha256OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hasher.AppendData(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    public async Task DownloadAsync(ModelInfo model, IProgress<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(modelsDirectory);
        var destination = LocalPath(model);
        var partPath = destination + ".part";

        try
        {
            using var response = await httpClient.GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;

            using (var httpStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            using (var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[BufferSize];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await httpStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                    hasher.AppendData(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    if (contentLength is > 0)
                    {
                        progress.Report((double)totalRead / contentLength.Value);
                    }
                }

                var actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                if (actual != model.Sha256)
                {
                    fileStream.Close();
                    DeleteIfExists(partPath);
                    throw new ChecksumMismatchException(model.Sha256, actual);
                }
            }

            File.Move(partPath, destination, overwrite: true);
            progress.Report(1.0);
        }
        catch (OperationCanceledException)
        {
            DeleteIfExists(partPath);
            throw;
        }
        catch (ChecksumMismatchException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DeleteIfExists(partPath);
            throw new ModelDownloadException($"Failed to download model '{model.Name}'.", ex);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
