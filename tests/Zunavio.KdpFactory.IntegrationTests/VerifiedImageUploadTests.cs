using Zunavio.KdpFactory.Web.Api;

namespace Zunavio.KdpFactory.IntegrationTests;

public sealed class VerifiedImageUploadTests
{
    [Fact]
    public void DetectMime_OnlyAcceptsKnownFileSignatures()
    {
        var png = new byte[45];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(png, 0);
        new byte[] { 0, 0, 0, 0, 0x49, 0x45, 0x4e, 0x44 }.CopyTo(png, png.Length - 12);
        var jpeg = new byte[16];
        new byte[] { 0xff, 0xd8, 0xff }.CopyTo(jpeg, 0);
        new byte[] { 0xff, 0xd9 }.CopyTo(jpeg, jpeg.Length - 2);
        var webp = new byte[20];
        new byte[] { 0x52, 0x49, 0x46, 0x46, 12, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }.CopyTo(webp, 0);
        Assert.Equal("image/png", VerifiedImageUploadService.DetectMime(png));
        Assert.Equal("image/jpeg", VerifiedImageUploadService.DetectMime(jpeg));
        Assert.Equal("image/webp", VerifiedImageUploadService.DetectMime(webp));
        Assert.Null(VerifiedImageUploadService.DetectMime([0xff, 0xd8, 0xff, 0]));
        Assert.Null(VerifiedImageUploadService.DetectMime("not an image"u8.ToArray()));
        Assert.Null(VerifiedImageUploadService.DetectMime([]));
    }
}
