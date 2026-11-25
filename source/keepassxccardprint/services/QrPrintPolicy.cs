using QRCoder;

namespace KeePassXCCardPrint.Services;

/// <summary>QR sizing is in physical millimetres, including the quiet zone
/// supplied by QRCoder's module matrix. Never shrink below the configured module pitch.</summary>
public static class QrPrintPolicy
{
    public static QRCodeGenerator.ECCLevel Correction(string code) => code switch
    {
        "L" => QRCodeGenerator.ECCLevel.L,
        "M" => QRCodeGenerator.ECCLevel.M,
        "H" => QRCodeGenerator.ECCLevel.H,
        _ => QRCodeGenerator.ECCLevel.Q
    };

    public static float RequiredSide(string payload, FieldRule rule, float availableWidth, float availableHeight)
    {
        if (string.IsNullOrEmpty(payload))
            throw new InvalidOperationException("Невозможно создать QR из пустого значения.");
        int count;
        try
        {
            using var qr = QRCodeGenerator.GenerateQrCode(payload, Correction(rule.QrEcc));
            count = qr.ModuleMatrix.Count;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidOperationException("Не удалось разместить значение в одном QR-коде.");
        }
        if (count <= 0) throw new InvalidOperationException("Не удалось сформировать QR-код.");
        float minModule = (float)(double.IsFinite(rule.QrMinModuleMm)
            ? Math.Clamp(rule.QrMinModuleMm, .35, .5) : .4);
        float mandatory = count * minModule;
        float target = (float)(double.IsFinite(rule.QrSizeMm) ? Math.Max(0, rule.QrSizeMm) : 0);
        float result = Math.Max(mandatory, target);
        if (result > availableWidth + .001f || result > availableHeight + .001f)
            throw new InvalidOperationException(
                $"QR не помещается: нужно минимум {result:0.0} × {result:0.0} мм " +
                $"({count} модулей, {minModule:0.00} мм/модуль). " +
                "Увеличьте карточку или уменьшите коррекцию ошибок / минимальный модуль.");
        return result;
    }
}
