using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Jarvis;

public static class AppVersion
{
    public static string Current =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static string FromExecutable(string exePath)
    {
        if (!File.Exists(exePath)) return "не установлена";

        var info = FileVersionInfo.GetVersionInfo(exePath);
        return string.IsNullOrWhiteSpace(info.ProductVersion)
            ? "неизвестно"
            : info.ProductVersion;
    }
}
