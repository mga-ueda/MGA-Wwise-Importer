namespace MgaWwiseIMImporter.Wwise;

/// <summary>Wwise へ作成する Interactive Music 構造の計画。</summary>
internal sealed class WwiseMusicPlan
{
    /// <summary>最上位に作るオブジェクト名（元ファイル名の拡張子抜き）。</summary>
    public required string ContainerName { get; init; }

    /// <summary>true なら Music Switch Container の下に複数 Playlist を作る。</summary>
    public required bool IsMultiPart { get; init; }

    public required IReadOnlyList<WwisePlaylistPlan> Playlists { get; init; }

    public int TotalSegmentCount => Playlists.Sum(p => p.Segments.Count);
}

/// <summary>Music Playlist Container 1 つ分。</summary>
internal sealed class WwisePlaylistPlan
{
    public required string Name { get; init; }

    /// <summary>
    /// 代表となるエクスポート WAV のフルパス（コピー／ログ用）。
    /// レイヤーグループ時は先頭メンバーのパス。
    /// </summary>
    public required string SourceWavPath { get; init; }

    public required IReadOnlyList<WwiseSegmentPlan> Segments { get; init; }
}

/// <summary>
/// Music Segment 1 つ分。時間はセグメント代表タイムライン基準の絶対 ms（先頭トラックのパート先頭基準）。
/// インポート時は各トラックを自身の Clip 範囲で切り出し、タイムライン先頭へ載せる。
/// </summary>
internal sealed class WwiseSegmentPlan
{
    public required string Name { get; init; }

    /// <summary>セグメント全体の可聴開始（代表タイムライン、通常は 0 相対の基準用）。</summary>
    public required double ClipStartMs { get; init; }

    /// <summary>セグメント全体の可聴終了（EndPosition 算出用）。</summary>
    public required double ClipEndMs { get; init; }

    /// <summary>Entry Cue の絶対時刻（-A があればアウフタクト明け）。</summary>
    public required double EntryCueMs { get; init; }

    /// <summary>Exit Cue の絶対時刻（-E があればその開始）。</summary>
    public required double ExitCueMs { get; init; }

    /// <summary>-L 区間なら true（Playlist Item を無限ループにする）。</summary>
    public required bool LoopInfinite { get; init; }

    public required double TempoBpm { get; init; }
    public required int TimeSignatureUpper { get; init; }
    public required int TimeSignatureLower { get; init; }

    /// <summary>単発マーカー由来の Custom Cue（名前は重複回避済み）。</summary>
    public required IReadOnlyList<WwiseCustomCue> CustomCues { get; init; }

    /// <summary>同一セグメント内で同時再生する Music Track（縦レイヤー）。</summary>
    public required IReadOnlyList<WwiseTrackPlan> Tracks { get; init; }
}

/// <summary>Music Track 1 つ分（1 つのソースパート WAV からの切り出し）。</summary>
internal sealed class WwiseTrackPlan
{
    public required string Name { get; init; }

    /// <summary>エクスポートされたパート WAV のフルパス（コピー前）。</summary>
    public required string SourceWavPath { get; init; }

    /// <summary>ソース WAV 内の可聴開始（切り出し開始）。</summary>
    public required double ClipStartMs { get; init; }

    /// <summary>ソース WAV 内の可聴終了（切り出し終了）。</summary>
    public required double ClipEndMs { get; init; }
}

/// <summary>Custom Cue 1 つ。</summary>
internal readonly record struct WwiseCustomCue(double TimeMs, string Name);
