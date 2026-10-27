namespace LolStatsCheck.Core.Util;

public static class PositionNames
{
    public static string ToChinese(string? position) => (position ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "top" => "上单",
        "jungle" => "打野",
        "middle" or "mid" => "中单",
        "bottom" or "adc" => "下路",
        "utility" or "support" => "辅助",
        _ => "待定"
    };
}
