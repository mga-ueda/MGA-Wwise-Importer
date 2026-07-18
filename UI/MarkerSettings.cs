using System.Globalization;
using MgaWwiseIMImporter.Wave;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// マウスドラッグで付与するマーカーのスナップ単位。
/// </summary>
internal enum MarkerGridOverrideMode
{
    /// <summary>見た目のグリッド単位（従来仕様）。</summary>
    Default,

    /// <summary>表示状態に関わらず常に小節単位。</summary>
    Bar,

    /// <summary>表示状態に関わらず常に拍単位。</summary>
    Beat,
}

/// <summary>
/// マーカー付与オプション（exe 横の MgaWwiseIMImporter.ini [Markers]）。
/// </summary>
internal sealed class MarkerSettings
{
    public const string Section = "Markers";
    private const int CurrentSettingsVersion = 3;

    /// <summary>ドラッグ付与時のスナップ単位。</summary>
    public MarkerGridOverrideMode GridOverride { get; set; } = MarkerGridOverrideMode.Default;

    /// <summary>連番の桁数（幅・上限。0 で連番なし）。</summary>
    public int CommentDigits { get; set; } = 3;

    /// <summary>Digits で指定した桁数まで 0 埋めするか（桁数に依らず同じ）。</summary>
    public bool CommentZeroPad { get; set; } = true;

    public bool CommentPrefixEnabled { get; set; } = true;

    public string CommentPrefix { get; set; } = string.Empty;

    public bool CommentSuffixEnabled { get; set; }

    public string CommentSuffix { get; set; } = string.Empty;

    /// <summary>接頭語・接尾語と連番を繋ぐ文字を使うか。</summary>
    public bool CommentJoinerEnabled { get; set; }

    public string CommentJoiner { get; set; } = "_";

    /// <summary>塊（書き出しパート）ごとに連番をリセットするか。</summary>
    public bool CommentResetPerPart { get; set; } = true;

    public const int CommentDigitsMin = 0;
    public const int CommentDigitsMax = 6;

    /// <summary>コメント生成側（Wave 層）に渡す確定値へ変換する。</summary>
    public MarkerCommentRule ToCommentRule() => new(
        Digits: Math.Clamp(CommentDigits, CommentDigitsMin, CommentDigitsMax),
        ZeroPad: CommentZeroPad,
        Prefix: CommentPrefixEnabled ? CommentPrefix : string.Empty,
        Suffix: CommentSuffixEnabled ? CommentSuffix : string.Empty,
        Joiner: CommentJoinerEnabled ? CommentJoiner : string.Empty,
        ResetPerPart: CommentResetPerPart);

    public static MarkerSettings Load()
    {
        var values = IniFile.ReadSection(Section);
        var settings = new MarkerSettings();
        var settingsVersion = values.TryGetValue("SettingsVersion", out var versionText)
            && int.TryParse(versionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
                ? version
                : 0;

        if (values.TryGetValue("GridOverride", out var grid)
            && Enum.TryParse<MarkerGridOverrideMode>(grid, ignoreCase: true, out var gridMode))
        {
            settings.GridOverride = gridMode;
        }

        if (values.TryGetValue("CommentDigits", out var digitsText)
            && int.TryParse(digitsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits))
        {
            settings.CommentDigits = Math.Clamp(digits, CommentDigitsMin, CommentDigitsMax);
        }

        settings.CommentZeroPad = ReadBool(values, "CommentZeroPad", settings.CommentZeroPad);
        settings.CommentPrefixEnabled = ReadBool(values, "CommentPrefixEnabled", settings.CommentPrefixEnabled);
        settings.CommentSuffixEnabled = ReadBool(values, "CommentSuffixEnabled", settings.CommentSuffixEnabled);
        settings.CommentJoinerEnabled = ReadBool(values, "CommentJoinerEnabled", settings.CommentJoinerEnabled);
        settings.CommentResetPerPart = ReadBool(values, "CommentResetPerPart", settings.CommentResetPerPart);

        if (values.TryGetValue("CommentPrefix", out var prefix))
        {
            settings.CommentPrefix = prefix;
        }

        if (values.TryGetValue("CommentSuffix", out var suffix))
        {
            settings.CommentSuffix = suffix;
        }

        if (values.TryGetValue("CommentJoiner", out var joiner))
        {
            settings.CommentJoiner = joiner;
        }

        // 旧版で自動生成された既定値を、新しい既定値へ一度だけ移行する。
        if (settingsVersion < CurrentSettingsVersion)
        {
            settings.CommentPrefix = string.Empty;
            settings.CommentPrefixEnabled = true;
            settings.CommentSuffix = string.Empty;
            settings.CommentZeroPad = true;
            settings.CommentResetPerPart = true;
            settings.Save();
        }

        return settings;
    }

    public void Save()
    {
        IniFile.WriteSection(Section, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SettingsVersion"] = CurrentSettingsVersion.ToString(CultureInfo.InvariantCulture),
            ["GridOverride"] = GridOverride.ToString(),
            ["CommentDigits"] = Math.Clamp(CommentDigits, CommentDigitsMin, CommentDigitsMax)
                .ToString(CultureInfo.InvariantCulture),
            ["CommentZeroPad"] = CommentZeroPad ? "1" : "0",
            ["CommentPrefixEnabled"] = CommentPrefixEnabled ? "1" : "0",
            ["CommentPrefix"] = CommentPrefix,
            ["CommentSuffixEnabled"] = CommentSuffixEnabled ? "1" : "0",
            ["CommentSuffix"] = CommentSuffix,
            ["CommentJoinerEnabled"] = CommentJoinerEnabled ? "1" : "0",
            ["CommentJoiner"] = CommentJoiner,
            ["CommentResetPerPart"] = CommentResetPerPart ? "1" : "0",
        });
    }

    private static bool ReadBool(
        Dictionary<string, string> values,
        string key,
        bool defaultValue)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return defaultValue;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number != 0;
        }

        if (bool.TryParse(text, out var flag))
        {
            return flag;
        }

        return defaultValue;
    }
}
