using System.Globalization;
using KeePassXC.CloudTransport;

namespace KeePassXCCardPrint.Services;

/// <summary>Pure UI presentation rules: never fabricate live progress for a CLI-only provider.</summary>
internal static class CloudTransferPresentation
{
    internal static bool Matches(CloudTransferProgress item, string step,
        string engineStatus, bool foreground)
    {
        if (foreground)
        {
            return step switch
            {
                "test.upload" or "sync.upload" => item.Direction == CloudTransferDirection.Upload,
                "test.download" or "sync.remote" or "sync.confirm" => item.Direction == CloudTransferDirection.Download,
                _ => false
            };
        }
        return engineStatus switch
        {
            "Uploading" => item.Direction == CloudTransferDirection.Upload,
            "Downloading" => item.Direction == CloudTransferDirection.Download,
            _ => false
        };
    }

    internal static double? Percent(CloudTransferProgress item)
        => item.HasLiveByteProgress && item.TotalBytes is > 0
            ? Math.Clamp(100d * item.TransferredBytes / item.TotalBytes.Value, 0d, 100d)
            : null;

    private static string Size(long bytes, string language)
    {
        var lang = language == "uk" ? "uk" : language == "en" ? "en" : "ru";
        var unit = lang == "en" ? new[] { "B", "KiB", "MiB", "GiB" }
                                 : new[] { "Б", "КиБ", "МиБ", "ГиБ" };
        var i = 0;
        double v = Math.Max(0, bytes);
        while (v >= 1024 && i < unit.Length - 1) { v /= 1024d; i++; }
        return (i == 0 ? v.ToString("0", CultureInfo.CurrentCulture) :
            v.ToString("0.0", CultureInfo.CurrentCulture)) + " " + unit[i];
    }

    internal static string Describe(CloudTransferProgress item, string language)
    {
        string L(string ru, string uk, string en) => language switch
        {
            "uk" => uk, "en" => en, _ => ru
        };
        if (item.Phase == CloudTransferPhase.Skipped)
            return L("Без передачи: файл не изменился", "Без передавання: файл не змінився", "Skipped: file unchanged");
        if (item.Phase == CloudTransferPhase.Completed)
            return L("Передано", "Передано", "Transferred") + ": " + Size(item.TransferredBytes, language);
        if (!item.HasLiveByteProgress)
            return L("Proton CLI выполняет передачу…", "Proton CLI виконує передавання…",
                "Proton CLI is transferring…");
        if (item.Phase == CloudTransferPhase.Preparing)
            return L("Подготовка передачи…", "Підготовка передавання…", "Preparing transfer…");
        if (item.Phase == CloudTransferPhase.Confirming)
            return L("Ожидание подтверждения сервера…", "Очікування підтвердження сервера…",
                "Waiting for server confirmation…");
        if (item.TotalBytes is > 0)
            return Size(item.TransferredBytes, language) + " / " +
                Size(item.TotalBytes.Value, language) + " · " +
                Percent(item)!.Value.ToString("0", CultureInfo.CurrentCulture) + "%";
        return L("Передано", "Передано", "Transferred") + ": " + Size(item.TransferredBytes, language);
    }
}
