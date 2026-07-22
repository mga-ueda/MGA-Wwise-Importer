namespace MgaWwiseIMImporter.Wwise;

/// <summary>
/// Wwise オブジェクト名の固定・整形。
/// </summary>
internal static class WwiseObjectNames
{
    /// <summary>複数波形モードの Music Switch / State Group 名。</summary>
    public const string MultiWaveContainerName = "Multi_Wave";

    /// <summary>複数波形モードの Music Switch / State Group 名を返す。</summary>
    public static string MakeMultiWaveContainerName() => MultiWaveContainerName;
}
