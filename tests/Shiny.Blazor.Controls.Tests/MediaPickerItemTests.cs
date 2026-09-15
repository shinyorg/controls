using System.Text;
using Shiny.Blazor.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;


/// <summary>
/// A picked photo's bytes stay in the browser until something asks for them, so the item is the part
/// of the picker that can be driven without a page: what it does with bytes it has, and what it does
/// when it has none.
/// </summary>
public class MediaPickerItemTests
{
    static MediaPickerItem WithBrowserBytes(byte[] bytes)
        => new()
        {
            Id = "photo-1",
            ContentType = "image/jpeg",
            Size = bytes.Length,
            Opener = (_, _) => Task.FromResult<Stream>(new MemoryStream(bytes, false))
        };


    [Fact]
    public async Task ReadsFromTheBrowserOnlyOnce()
    {
        var reads = 0;
        var item = new MediaPickerItem
        {
            Opener = (_, _) =>
            {
                reads++;
                return Task.FromResult<Stream>(new MemoryStream("hello"u8.ToArray(), false));
            }
        };

        var first = await item.ReadAllBytesAsync();
        var second = await item.ReadAllBytesAsync();

        Encoding.UTF8.GetString(first).ShouldBe("hello");
        second.ShouldBeSameAs(first);
        item.HasData.ShouldBeTrue();
        reads.ShouldBe(1);
    }


    [Fact]
    public async Task StreamsWhatItAlreadyHasWithoutTheBrowser()
    {
        var item = new MediaPickerItem { Data = "already here"u8.ToArray() };

        await using var stream = await item.OpenReadStreamAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        Encoding.UTF8.GetString(buffer.ToArray()).ShouldBe("already here");
    }


    [Fact]
    public async Task PassesTheSizeLimitThrough()
    {
        long? asked = null;
        var item = new MediaPickerItem
        {
            Opener = (max, _) =>
            {
                asked = max;
                return Task.FromResult<Stream>(new MemoryStream([1, 2, 3], false));
            }
        };

        await item.ReadAllBytesAsync(1234);

        asked.ShouldBe(1234);
    }


    [Fact]
    public async Task SaysSoWhenTheBytesAreGone()
    {
        var item = new MediaPickerItem { Id = "photo-1" };

        await Should.ThrowAsync<InvalidOperationException>(() => item.OpenReadStreamAsync());
    }


    [Fact]
    public async Task ReadsWhatTheBrowserHolds()
    {
        var item = WithBrowserBytes([9, 8, 7]);

        (await item.ReadAllBytesAsync()).ShouldBe([9, 8, 7]);
        item.Size.ShouldBe(3);
    }


    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(50, 200, 25)]
    [InlineData(200, 200, 100)]
    public void ProgressIsAPercentageOnlyWhenTheTotalIsKnown(long sent, long total, int? expected)
    {
        var progress = new MediaPickerUploadProgress(new MediaPickerItem(), sent, total);

        progress.Percent.ShouldBe(expected);
    }


    [Fact]
    public void AnUploadDefaultsToAPlainFilePost()
    {
        var upload = new MediaPickerUpload("/photos");

        upload.Method.ShouldBe("POST");
        upload.FieldName.ShouldBe("file");
        upload.WithCredentials.ShouldBeFalse();
        upload.Headers.ShouldBeNull();
    }
}
