namespace MgaWwiseIMImporter.Wwise;

/// <summary>
/// Wwise へのインポート設定（アプリ内固定。INI には書かない）。
/// LookAhead／Prefetch はプロジェクト設定（[Project.*]）。
/// </summary>
internal sealed class WwiseImportSettings
{
    public const string DefaultStateGroupParentPath = @"\States\Default Work Unit";
    public const int DefaultLookAheadMs = 500;
    public const int DefaultPrefetchLengthMs = 500;

    /// <summary>Music Track のストリーミング有効（既定オン）。</summary>
    public bool StreamEnabled { get; init; } = true;

    /// <summary>2 番目以降のセグメントの Look-ahead time（ms）。</summary>
    public int LookAheadMs { get; init; } = DefaultLookAheadMs;

    /// <summary>Playlist 先頭セグメント内全トラックの Prefetch Length（ms）。ストリーミング時に有効。</summary>
    public int PrefetchLengthMs { get; init; } = DefaultPrefetchLengthMs;

    /// <summary>
    /// 複数パート時に作る State Group の親パス。
    /// 既定は <c>\States\Default Work Unit</c>。
    /// </summary>
    public string StateGroupParentPath { get; init; } = DefaultStateGroupParentPath;

    public string ResolveStateGroupPath(string groupName)
    {
        var parent = StateGroupParentPath.Trim().TrimEnd('\\');
        if (parent.Length == 0)
        {
            parent = DefaultStateGroupParentPath;
        }

        return $"{parent}\\{groupName}";
    }

    public WwiseImportSettings WithStreaming(
        bool streamEnabled,
        int lookAheadMs,
        int prefetchLengthMs) =>
        new()
        {
            StreamEnabled = streamEnabled,
            LookAheadMs = Math.Clamp(lookAheadMs, 0, 9999),
            PrefetchLengthMs = Math.Clamp(prefetchLengthMs, 0, 9999),
            StateGroupParentPath = StateGroupParentPath,
        };

    /// <summary>アプリ固定値を返す。</summary>
    public static WwiseImportSettings Load() => new();
}
