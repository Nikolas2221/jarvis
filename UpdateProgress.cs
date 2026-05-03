namespace Jarvis;

public sealed record UpdateProgress(
    string Message,
    long? BytesDownloaded = null,
    long? TotalBytes = null)
{
    public int? Percent =>
        TotalBytes is > 0 && BytesDownloaded != null
            ? (int)Math.Clamp(BytesDownloaded.Value * 100 / TotalBytes.Value, 0, 100)
            : null;

    public string DisplayText
    {
        get
        {
            if (BytesDownloaded == null || TotalBytes == null)
            {
                return Message;
            }

            return $"{Message} {FormatBytes(BytesDownloaded.Value)} / {FormatBytes(TotalBytes.Value)}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        var mb = bytes / 1024d / 1024d;
        return $"{mb:0.0} МБ";
    }
}
