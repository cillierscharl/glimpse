using Glimpse.Helpers;
using Xunit;

namespace Glimpse.Tests;

public class FileHelperTests
{
    [Theory]
    [InlineData("image.png", "image/png")]
    [InlineData("image.PNG", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("photo.JPEG", "image/jpeg")]
    [InlineData("image.webp", "image/webp")]
    [InlineData("image.gif", "image/gif")]
    [InlineData("file.txt", "application/octet-stream")]
    [InlineData("file.unknown", "application/octet-stream")]
    public void GetMimeType_ReturnsCorrectType(string path, string expectedMime)
    {
        var result = FileHelper.GetMimeType(path);
        Assert.Equal(expectedMime, result);
    }

    [Fact]
    public async Task GetBase64DataUrlAsync_ReturnsNullForNonExistentFile()
    {
        var result = await FileHelper.GetBase64DataUrlAsync("/nonexistent/file.png");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetBase64DataUrlAsync_ReturnsDataUrl()
    {
        // Create a temp file
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.png");
        try
        {
            await File.WriteAllBytesAsync(tempPath, [0x89, 0x50, 0x4E, 0x47]); // PNG header bytes

            var result = await FileHelper.GetBase64DataUrlAsync(tempPath);

            Assert.NotNull(result);
            Assert.StartsWith("data:image/png;base64,", result);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
