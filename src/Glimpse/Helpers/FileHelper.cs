namespace Glimpse.Helpers;

public static class FileHelper
{
    /// <summary>
    /// Gets the MIME type for a file based on its extension.
    /// </summary>
    public static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Reads a file and returns it as a base64 data URL.
    /// Returns null if the file doesn't exist or can't be read.
    /// </summary>
    public static async Task<string?> GetBase64DataUrlAsync(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var mimeType = GetMimeType(path);
            return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }
}
