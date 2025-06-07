using Microsoft.Maui.Media;

namespace SymbolCollector.Android.Library;

internal class ScreenshotAttachment : SentryAttachment
{
    public ScreenshotAttachment()
        : this(
            AttachmentType.Default,
            new ScreenshotAttachmentContent(),
            "screenshot.jpg",
            "image/jpeg")
    {
    }

    private ScreenshotAttachment(
        AttachmentType type,
        IAttachmentContent content,
        string fileName,
        string? contentType)
        : base(type, content, fileName, contentType)
    {
    }
}
internal class ScreenshotAttachmentContent : IAttachmentContent
{
    public Stream GetStream()
    {
        var screenStream = Screenshot.CaptureAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        return screenStream.OpenReadAsync(ScreenshotFormat.Jpeg).ConfigureAwait(false).GetAwaiter().GetResult();
    }
}
