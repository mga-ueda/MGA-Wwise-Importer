using System.Globalization;

namespace MgaWwiseIMImporter.Wave;

/// <summary>
/// UI で追加したマーカーのコメント（連番）生成ルール。
/// 接頭語・接尾語・連結文字は「無し」の場合に空文字で渡す。
/// </summary>
internal readonly record struct MarkerCommentRule(
    int Digits,
    bool ZeroPad,
    string Prefix,
    string Suffix,
    string Joiner,
    bool ResetPerPart)
{
    public static MarkerCommentRule Default { get; } = new(
        Digits: 3,
        ZeroPad: true,
        Prefix: string.Empty,
        Suffix: string.Empty,
        Joiner: string.Empty,
        ResetPerPart: false);

    public string Format(int number)
    {
        var parts = new List<string>(3);
        if (Prefix.Length > 0)
        {
            parts.Add(Prefix);
        }

        if (Digits > 0)
        {
            var numberText = number.ToString(CultureInfo.InvariantCulture);
            if (ZeroPad)
            {
                numberText = numberText.PadLeft(Digits, '0');
            }

            parts.Add(numberText);
        }

        if (Suffix.Length > 0)
        {
            parts.Add(Suffix);
        }

        return string.Join(Joiner, parts);
    }

    /// <summary>
    /// 連番ありのときの最大値（Digits=3 → 999）。連番なしは制限しない。
    /// </summary>
    public int MaxSerialNumber =>
        Digits <= 0
            ? int.MaxValue
            : (int)Math.Pow(10, Digits) - 1;
}

/// <summary>
/// 読み込み元の不変プレビューと、現在の読み込み中だけ有効な追加マーカーを管理する。
/// </summary>
internal sealed class WaveformPreviewSession
{
    private readonly List<UserWaveformMarker> _userMarkers = [];
    private readonly List<WaveformMarkerMark>? _waveOnlyMarkers;
    private IReadOnlyList<WaveformRegionMark>? _waveOnlyRegions;
    private IReadOnlyList<WaveformOutputPart>? _waveOnlyOutputParts;
    /// <summary>
    /// マーカー 2 つ特例のコメント実体化を一度行ったら true。
    /// 以降はユーザー編集を上書きしない。個数が 2 以外になったらクリア。
    /// </summary>
    private bool _twoMarkerLoopCommentsMaterialized;
    private readonly List<WaveOnlyModeProcessor.MarkerCommentRename> _pendingWaveMarkerRenames = [];
    private readonly Dictionary<int, WaveformOutputPart> _markerShareAnchorByPartNumber = [];
    private readonly List<IReadOnlyList<WaveformOutputPart>> _markerShareGroups = [];
    private HashSet<int> _disabledPartNumbers = [];
    private IReadOnlyList<WaveformMarkerMark> _effectiveMarkers;
    private IReadOnlyList<WaveformMarkerMark> _wwiseMarkers;
    private MarkerCommentRule _commentRule = MarkerCommentRule.Default;
    private IReadOnlyList<RegionEdgeFade> _regionEdgeFades = [];

    public WaveformPreviewSession(WaveformPreviewData preview)
    {
        Preview = preview;
        if (preview.AllowsSessionMarkerEdit)
        {
            _waveOnlyMarkers = preview.Markers.ToList();
            _effectiveMarkers = _waveOnlyMarkers;
            // Wave 単体モードのマーカーは表示・リージョン構築のみ。Wwise Custom Cue には出さない。
            _wwiseMarkers = [];
            RebuildWaveOnlyRegions();
        }
        else
        {
            _waveOnlyMarkers = null;
            _waveOnlyRegions = null;
            _waveOnlyOutputParts = null;
            _effectiveMarkers = preview.Markers;
            _wwiseMarkers = preview.Markers;
        }
    }

    /// <summary>Wave 単体モードでアプリ上のマーカー編集が可能なとき true。</summary>
    public bool AllowsSessionMarkerEdit => _waveOnlyMarkers is not null;

    /// <summary>コメント生成ルールを差し替え、既存の追加マーカーへ再適用する。</summary>
    public void SetCommentRule(MarkerCommentRule rule)
    {
        if (_commentRule == rule)
        {
            return;
        }

        _commentRule = rule;
        if (_userMarkers.Count > 0)
        {
            RebuildMarkerSnapshots();
        }
    }

    public WaveformPreviewData Preview { get; }

    public IReadOnlyList<WaveformMarkerMark> EffectiveMarkers => _effectiveMarkers;

    public IReadOnlyList<WaveformMarkerMark> WwiseMarkers => _wwiseMarkers;

    /// <summary>
    /// 有効なリージョン。Wave 単体モードではマーカーコメントから再構築したものを返す。
    /// </summary>
    public IReadOnlyList<WaveformRegionMark> EffectiveRegions =>
        _waveOnlyRegions ?? Preview.Regions;

    /// <summary>
    /// 有効な出力パート。Wave 単体モードではリージョンから再構築したものを返す。
    /// </summary>
    public IReadOnlyList<WaveformOutputPart> EffectiveOutputParts =>
        _waveOnlyOutputParts ?? Preview.OutputParts;

    /// <summary>
    /// 連続リージョン固まりのイン／アウト端フェード（プレビュー用・非破壊）。
    /// </summary>
    public IReadOnlyList<RegionEdgeFade> RegionEdgeFades => _regionEdgeFades;

    /// <summary>フェード一覧を差し替える（正規化＋現行リージョンへ再マップ）。</summary>
    public void SetRegionEdgeFades(IReadOnlyList<RegionEdgeFade>? fades)
    {
        _regionEdgeFades = RegionEdgeFade.RemapToRuns(
            fades ?? [],
            EffectiveRegions);
    }

    /// <summary>1 固まり分のフェードを更新する。長さ 0 なら一覧から外す。</summary>
    public void UpsertRegionEdgeFade(RegionEdgeFade fade)
    {
        var normalized = fade.Normalized();
        var next = _regionEdgeFades
            .Where(existing =>
                existing.InSample != normalized.InSample
                || existing.OutSample != normalized.OutSample)
            .ToList();
        if (normalized.HasAnyFade)
        {
            next.Add(normalized);
        }

        _regionEdgeFades = RegionEdgeFade.RemapToRuns(next, EffectiveRegions);
    }

    /// <summary>
    /// アプリ追加マーカーのサンプル位置（投影は含まない実体のみ）。
    /// </summary>
    public IReadOnlyList<long> GetUserMarkerSampleOffsets() =>
        _userMarkers
            .Select(marker => marker.SampleOffset)
            .OrderBy(sampleOffset => sampleOffset)
            .ToArray();

    /// <summary>
    /// 無効パート番号を反映する。無効範囲へのマーカー追加／削除とグループ投影を抑止する。
    /// マーカー実体は保持し、再有効化後に復帰する。
    /// </summary>
    public void SetDisabledPartNumbers(IEnumerable<int>? partNumbers)
    {
        var next = partNumbers?.ToHashSet() ?? [];
        if (_disabledPartNumbers.SetEquals(next))
        {
            return;
        }

        _disabledPartNumbers = next;
        RebuildMarkerSnapshots();
    }

    /// <summary>
    /// Playlist グループを反映する。2 件以上のグループでは最小 part 番号のマーカーを基準に、
    /// Playlist 先頭からの相対位置とコメントを全メンバーへ投影する。
    /// </summary>
    public void SetPlaylistGroups(IReadOnlyDictionary<int, int>? partGroupIds)
    {
        _markerShareAnchorByPartNumber.Clear();
        _markerShareGroups.Clear();

        if (partGroupIds is { Count: > 0 })
        {
            var groups = new Dictionary<int, List<WaveformOutputPart>>();
            foreach (var part in Preview.OutputParts)
            {
                if (_disabledPartNumbers.Contains(part.Number)
                    || !partGroupIds.TryGetValue(part.Number, out var groupId))
                {
                    continue;
                }

                if (!groups.TryGetValue(groupId, out var members))
                {
                    members = [];
                    groups[groupId] = members;
                }

                members.Add(part);
            }

            foreach (var members in groups.Values)
            {
                if (members.Count < 2)
                {
                    continue;
                }

                members.Sort((a, b) => a.Number.CompareTo(b.Number));
                var anchor = members[0];
                _markerShareGroups.Add(members);
                foreach (var member in members)
                {
                    _markerShareAnchorByPartNumber[member.Number] = anchor;
                }
            }
        }

        RebuildMarkerSnapshots();
    }

    public bool AddMarkers(IEnumerable<long> sampleOffsets)
    {
        var existing = _userMarkers.Select(marker => marker.SampleOffset).ToHashSet();
        var changed = false;
        // Reset Per Part 時はパートごとの追加済み件数を走査中に加算する。
        var addedInScope = new Dictionary<int, int>();
        foreach (var requestedSampleOffset in sampleOffsets.Distinct())
        {
            if (!TryResolveSharedMarkerSample(requestedSampleOffset, out var sampleOffset))
            {
                continue;
            }

            if (!IsEditableMarkerSample(sampleOffset)
                || !existing.Add(sampleOffset))
            {
                continue;
            }

            if (!CanAllocateSerialNumber(sampleOffset, addedInScope))
            {
                existing.Remove(sampleOffset);
                continue;
            }

            _userMarkers.Add(new UserWaveformMarker(Guid.NewGuid(), sampleOffset));
            changed = true;
        }

        if (changed)
        {
            RebuildMarkerSnapshots();
        }

        return changed;
    }

    /// <summary>
    /// Digits 桁に収まる連番がまだ余っているか。
    /// </summary>
    private bool CanAllocateSerialNumber(
        long sampleOffset,
        Dictionary<int, int> addedInScope)
    {
        if (_commentRule.Digits <= 0)
        {
            return true;
        }

        var scopeKey = _commentRule.ResetPerPart
            ? FindNumberingPartIndex(sampleOffset)
            : -1;
        if (!addedInScope.TryGetValue(scopeKey, out var pendingInScope))
        {
            pendingInScope = 0;
        }

        var existingInScope = CountAuthoritativeMarkersInScope(scopeKey);
        if (existingInScope + pendingInScope >= _commentRule.MaxSerialNumber)
        {
            return false;
        }

        addedInScope[scopeKey] = pendingInScope + 1;
        return true;
    }

    private int CountAuthoritativeMarkersInScope(int scopeKey)
    {
        var count = 0;
        foreach (var marker in _userMarkers)
        {
            if (!IsAuthoritativeUserMarkerSample(marker.SampleOffset)
                || IsMarkerOnDisabledPart(marker.SampleOffset))
            {
                continue;
            }

            if (_commentRule.ResetPerPart
                && FindNumberingPartIndex(marker.SampleOffset) != scopeKey)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public bool RemoveMarkers(IEnumerable<long> sampleOffsets)
    {
        var removals = sampleOffsets
            .Select(sampleOffset =>
                TryResolveSharedMarkerSample(sampleOffset, out var sharedSample)
                    ? (long?)sharedSample
                    : null)
            .Where(sampleOffset => sampleOffset.HasValue)
            .Select(sampleOffset => sampleOffset!.Value)
            .ToHashSet();
        if (removals.Count == 0)
        {
            return false;
        }

        var removed = _userMarkers.RemoveAll(marker => removals.Contains(marker.SampleOffset));
        if (removed > 0)
        {
            RebuildMarkerSnapshots();
        }

        return removed > 0;
    }

    /// <summary>指定サンプル位置に Wave 単体マーカーがあるか。</summary>
    public bool HasWaveOnlyMarkerAt(long sampleOffset) =>
        _waveOnlyMarkers is not null
        && _waveOnlyMarkers.Any(marker => marker.SampleOffset == sampleOffset);

    /// <summary>
    /// Wave 単体モードの埋め込みマーカーをアプリ上だけ追加する（WAV 非書き込み）。
    /// </summary>
    public bool TryAddWaveOnlyMarker(long sampleOffset, string comment = "")
    {
        if (_waveOnlyMarkers is null)
        {
            return false;
        }

        if (sampleOffset < 0 || sampleOffset >= Preview.WavInfo.FrameCount)
        {
            return false;
        }

        if (_waveOnlyMarkers.Any(marker => marker.SampleOffset == sampleOffset))
        {
            return false;
        }

        _waveOnlyMarkers.Add(new WaveformMarkerMark(sampleOffset, comment.Trim()));
        _waveOnlyMarkers.Sort((a, b) => a.SampleOffset.CompareTo(b.SampleOffset));
        RebuildMarkerSnapshots();
        RebuildWaveOnlyRegions();
        return true;
    }

    /// <summary>
    /// Wave 単体モードの埋め込みマーカーをアプリ上だけ移動する（WAV 非書き込み）。
    /// </summary>
    public bool TryMoveWaveOnlyMarker(long fromSampleOffset, long toSampleOffset)
    {
        if (_waveOnlyMarkers is null)
        {
            return false;
        }

        if (fromSampleOffset == toSampleOffset)
        {
            return false;
        }

        if (toSampleOffset < 0 || toSampleOffset >= Preview.WavInfo.FrameCount)
        {
            return false;
        }

        var index = _waveOnlyMarkers.FindIndex(marker => marker.SampleOffset == fromSampleOffset);
        if (index < 0)
        {
            return false;
        }

        if (_waveOnlyMarkers.Any(marker => marker.SampleOffset == toSampleOffset))
        {
            return false;
        }

        var existing = _waveOnlyMarkers[index];
        _waveOnlyMarkers[index] = new WaveformMarkerMark(
            toSampleOffset,
            existing.Comment,
            existing.IsSharedProjection,
            existing.IsFromWaveEmbedded);
        _waveOnlyMarkers.Sort((a, b) => a.SampleOffset.CompareTo(b.SampleOffset));
        RebuildMarkerSnapshots();
        RebuildWaveOnlyRegions();
        return true;
    }

    /// <summary>
    /// 指定マーカーと、その一つ前のマーカーを同じサンプル差分だけ移動する。
    /// 前マーカーが無い／範囲外／衝突するときは false。
    /// </summary>
    public bool TryMoveWaveOnlyMarkerWithPrevious(long fromSampleOffset, long toSampleOffset)
    {
        if (_waveOnlyMarkers is null)
        {
            return false;
        }

        var delta = toSampleOffset - fromSampleOffset;
        if (delta == 0)
        {
            return false;
        }

        var frameCount = Preview.WavInfo.FrameCount;
        if (toSampleOffset < 0 || toSampleOffset >= frameCount)
        {
            return false;
        }

        var fromIndex = _waveOnlyMarkers.FindIndex(marker => marker.SampleOffset == fromSampleOffset);
        if (fromIndex < 0)
        {
            return false;
        }

        long? previousSample = null;
        var previousIndex = -1;
        for (var i = 0; i < _waveOnlyMarkers.Count; i++)
        {
            var sample = _waveOnlyMarkers[i].SampleOffset;
            if (sample >= fromSampleOffset)
            {
                continue;
            }

            if (previousSample is null || sample > previousSample.Value)
            {
                previousSample = sample;
                previousIndex = i;
            }
        }

        if (previousSample is not { } prevFrom || previousIndex < 0)
        {
            return TryMoveWaveOnlyMarker(fromSampleOffset, toSampleOffset);
        }

        var prevTo = prevFrom + delta;
        if (prevTo < 0 || prevTo >= frameCount)
        {
            return false;
        }

        if (_waveOnlyMarkers.Any(marker =>
                marker.SampleOffset == toSampleOffset
                && marker.SampleOffset != fromSampleOffset
                && marker.SampleOffset != prevFrom))
        {
            return false;
        }

        if (_waveOnlyMarkers.Any(marker =>
                marker.SampleOffset == prevTo
                && marker.SampleOffset != fromSampleOffset
                && marker.SampleOffset != prevFrom))
        {
            return false;
        }

        var fromMarker = _waveOnlyMarkers[fromIndex];
        var prevMarker = _waveOnlyMarkers[previousIndex];
        _waveOnlyMarkers[fromIndex] = new WaveformMarkerMark(
            toSampleOffset,
            fromMarker.Comment,
            fromMarker.IsSharedProjection,
            fromMarker.IsFromWaveEmbedded);
        _waveOnlyMarkers[previousIndex] = new WaveformMarkerMark(
            prevTo,
            prevMarker.Comment,
            prevMarker.IsSharedProjection,
            prevMarker.IsFromWaveEmbedded);
        _waveOnlyMarkers.Sort((a, b) => a.SampleOffset.CompareTo(b.SampleOffset));
        RebuildMarkerSnapshots();
        RebuildWaveOnlyRegions();
        return true;
    }

    /// <summary>
    /// Wave 単体モードの埋め込みマーカーコメントをアプリ上だけ更新する（WAV 非書き込み）。
    /// </summary>
    public bool TrySetWaveOnlyMarkerComment(long sampleOffset, string comment)
    {
        if (_waveOnlyMarkers is null)
        {
            return false;
        }

        var index = _waveOnlyMarkers.FindIndex(marker => marker.SampleOffset == sampleOffset);
        if (index < 0)
        {
            return false;
        }

        var nextComment = comment.Trim();
        if (string.Equals(_waveOnlyMarkers[index].Comment, nextComment, StringComparison.Ordinal))
        {
            return false;
        }

        var existing = _waveOnlyMarkers[index];
        _waveOnlyMarkers[index] = new WaveformMarkerMark(
            sampleOffset,
            nextComment,
            existing.IsSharedProjection,
            existing.IsFromWaveEmbedded);
        RebuildMarkerSnapshots();
        RebuildWaveOnlyRegions();
        return true;
    }

    /// <summary>
    /// Wave 単体モードの埋め込みマーカーをアプリ上だけ削除する（WAV 非書き込み）。
    /// </summary>
    public bool TryRemoveWaveOnlyMarker(long sampleOffset)
    {
        if (_waveOnlyMarkers is null)
        {
            return false;
        }

        var removed = _waveOnlyMarkers.RemoveAll(marker => marker.SampleOffset == sampleOffset);
        if (removed <= 0)
        {
            return false;
        }

        RebuildMarkerSnapshots();
        RebuildWaveOnlyRegions();
        return true;
    }

    /// <summary>
    /// Wave 単体モードの現在マーカー（サンプル位置＋コメント）。非対象時は null。
    /// </summary>
    public IReadOnlyList<WaveformMarkerMark>? GetWaveOnlySessionMarkers() =>
        _waveOnlyMarkers is null ? null : _waveOnlyMarkers.ToArray();

    /// <summary>
    /// サイドカーから Wave 単体マーカー状態を復元する。リスト全体で置き換える。
    /// </summary>
    public bool TryReplaceWaveOnlySessionMarkers(IEnumerable<WaveformMarkerMark> markers)
    {
        if (_waveOnlyMarkers is null)
        {
            return false;
        }

        var next = markers
            .Where(marker =>
                marker.SampleOffset >= 0
                && marker.SampleOffset < Preview.WavInfo.FrameCount)
            .GroupBy(marker => marker.SampleOffset)
            .Select(group =>
            {
                var last = group.Last();
                return new WaveformMarkerMark(
                    group.Key,
                    last.Comment.Trim(),
                    last.IsSharedProjection,
                    last.IsFromWaveEmbedded);
            })
            .OrderBy(marker => marker.SampleOffset)
            .ToList();

        if (_waveOnlyMarkers.Count == next.Count
            && _waveOnlyMarkers.Zip(next).All(pair =>
                pair.First.SampleOffset == pair.Second.SampleOffset
                && string.Equals(pair.First.Comment, pair.Second.Comment, StringComparison.Ordinal)
                && pair.First.IsFromWaveEmbedded == pair.Second.IsFromWaveEmbedded))
        {
            return false;
        }

        _waveOnlyMarkers.Clear();
        _waveOnlyMarkers.AddRange(next);
        // サイドカー等からの復元コメントを、2 つ特例の再実体化で上書きしない。
        _twoMarkerLoopCommentsMaterialized = next
            .Where(marker =>
                marker.SampleOffset >= 0
                && marker.SampleOffset < Preview.WavInfo.FrameCount)
            .Select(marker => marker.SampleOffset)
            .Distinct()
            .Count() == 2;
        RebuildMarkerSnapshots();
        RebuildWaveOnlyRegions();
        return true;
    }

    private void RebuildWaveOnlyRegions()
    {
        if (_waveOnlyMarkers is null)
        {
            _waveOnlyRegions = null;
            _waveOnlyOutputParts = null;
            return;
        }

        MaterializeImplicitLoopComments();

        _waveOnlyRegions = WaveOnlyModeProcessor.BuildRegionsFromMarkers(
            _waveOnlyMarkers,
            Preview.WavInfo.FrameCount);
        _waveOnlyOutputParts = _waveOnlyRegions.Count == 0
            ? []
            : WaveformRegionBuilder.BuildOutputParts(_waveOnlyRegions, Preview.SourcePath);
        _regionEdgeFades = RegionEdgeFade.RemapToRuns(_regionEdgeFades, _waveOnlyRegions);
    }

    /// <summary>
    /// 暗黙ループを <c>-L</c> / <c>-E</c> コメントへ実体化する（WAV 非書き込み）。
    /// </summary>
    private void MaterializeImplicitLoopComments()
    {
        if (_waveOnlyMarkers is null)
        {
            return;
        }

        _pendingWaveMarkerRenames.Clear();

        var frameCount = Preview.WavInfo.FrameCount;
        var orderedCount = _waveOnlyMarkers
            .Where(marker => marker.SampleOffset >= 0 && marker.SampleOffset < frameCount)
            .Select(marker => marker.SampleOffset)
            .Distinct()
            .Count();

        if (orderedCount == 2)
        {
            if (!_twoMarkerLoopCommentsMaterialized)
            {
                WaveOnlyModeProcessor.TryMaterializeImplicitLoopComments(
                    _waveOnlyMarkers,
                    frameCount,
                    allowTwoMarkerMaterialize: true,
                    _pendingWaveMarkerRenames);
                _twoMarkerLoopCommentsMaterialized = true;
            }

            return;
        }

        _twoMarkerLoopCommentsMaterialized = false;
        WaveOnlyModeProcessor.TryMaterializeImplicitLoopComments(
            _waveOnlyMarkers,
            frameCount,
            allowTwoMarkerMaterialize: false,
            _pendingWaveMarkerRenames);
    }

    /// <summary>
    /// 直近の暗黙リネーム（埋め込みマーカーの Loop→-L 等）を取り出し、キューを空にする。
    /// </summary>
    public IReadOnlyList<WaveOnlyModeProcessor.MarkerCommentRename> TakePendingWaveMarkerRenames()
    {
        if (_pendingWaveMarkerRenames.Count == 0)
        {
            return [];
        }

        var taken = _pendingWaveMarkerRenames.ToArray();
        _pendingWaveMarkerRenames.Clear();
        return taken;
    }

    /// <summary>
    /// グループ内の編集位置を、最小 part 番号の基準 Playlist 上の同じ相対位置へ変換する。
    /// </summary>
    private bool TryResolveSharedMarkerSample(long requestedSampleOffset, out long sampleOffset)
    {
        sampleOffset = requestedSampleOffset;
        var part = Preview.OutputParts
            .Where(candidate =>
                requestedSampleOffset >= candidate.StartSampleOffset
                && requestedSampleOffset < candidate.EndSampleOffset)
            .Select(candidate => (WaveformOutputPart?)candidate)
            .FirstOrDefault();
        if (part is not { } matchedPart
            || !_markerShareAnchorByPartNumber.TryGetValue(matchedPart.Number, out var anchor))
        {
            return true;
        }

        var relativeSample = requestedSampleOffset - matchedPart.StartSampleOffset;
        var resolved = anchor.StartSampleOffset + relativeSample;
        if (resolved < anchor.StartSampleOffset || resolved >= anchor.EndSampleOffset)
        {
            return false;
        }

        sampleOffset = resolved;
        return true;
    }

    private bool IsEditableMarkerSample(long sampleOffset)
    {
        if (sampleOffset < 0 || sampleOffset >= Preview.WavInfo.FrameCount)
        {
            return false;
        }

        var hostPart = Preview.OutputParts
            .Where(part =>
                sampleOffset >= part.StartSampleOffset
                && sampleOffset < part.EndSampleOffset)
            .Select(part => (WaveformOutputPart?)part)
            .FirstOrDefault();
        if (hostPart is not { } part)
        {
            return false;
        }

        if (_disabledPartNumbers.Contains(part.Number))
        {
            return false;
        }

        return !Preview.Regions.Any(region =>
            sampleOffset >= region.StartSampleOffset
            && sampleOffset < region.EndSampleOffset
            && (string.Equals(region.NameSuffix, "-A", StringComparison.OrdinalIgnoreCase)
                || string.Equals(region.NameSuffix, "-E", StringComparison.OrdinalIgnoreCase)));
    }

    private void RebuildMarkerSnapshots()
    {
        // 連番は「権威ある」追加マーカーだけを数える。
        // グループ同期先へ投影される側は、基準 Playlist 上のマーカーとコメントを共有する。
        var orderedUserMarkers = _userMarkers
            .Where(marker =>
                IsAuthoritativeUserMarkerSample(marker.SampleOffset)
                && !IsMarkerOnDisabledPart(marker.SampleOffset))
            .OrderBy(marker => marker.SampleOffset)
            .ToArray();
        var userMarkerMarks = new WaveformMarkerMark[orderedUserMarkers.Length];
        var globalNumber = 0;
        var partNumber = 0;
        var currentPartIndex = -1;
        for (var i = 0; i < orderedUserMarkers.Length; i++)
        {
            var marker = orderedUserMarkers[i];
            globalNumber++;
            var number = globalNumber;
            if (_commentRule.ResetPerPart)
            {
                // グループは基準 Playlist を 1 つのパートとして数え、同期先では連番をリセットしない。
                var partIndex = FindNumberingPartIndex(marker.SampleOffset);
                if (partIndex != currentPartIndex)
                {
                    currentPartIndex = partIndex;
                    partNumber = 0;
                }

                partNumber++;
                number = partNumber;
            }

            userMarkerMarks[i] = new WaveformMarkerMark(
                marker.SampleOffset,
                _commentRule.Format(number));
        }

        var sourceMarkers = _waveOnlyMarkers is not null
            ? _waveOnlyMarkers
            : Preview.Markers;
        var baseMarkers = sourceMarkers
            .Concat(userMarkerMarks)
            .Where(marker => !IsMarkerOnDisabledPart(marker.SampleOffset))
            .OrderBy(marker => marker.SampleOffset)
            .ToArray();

        if (_markerShareGroups.Count == 0)
        {
            _effectiveMarkers = baseMarkers;
            // Wave 単体モードは Custom Cue に出さない（表示用 EffectiveMarkers のみ更新）。
            _wwiseMarkers = _waveOnlyMarkers is not null ? [] : _effectiveMarkers;
            return;
        }

        var groupedPartNumbers = _markerShareGroups
            .SelectMany(group => group)
            .Select(part => part.Number)
            .ToHashSet();
        var sharedMarkers = baseMarkers
            .Where(marker => !Preview.OutputParts.Any(part =>
                groupedPartNumbers.Contains(part.Number)
                && marker.SampleOffset >= part.StartSampleOffset
                && marker.SampleOffset < part.EndSampleOffset))
            .ToList();

        foreach (var group in _markerShareGroups)
        {
            var anchor = group[0];
            var anchorMarkers = baseMarkers
                .Where(marker =>
                    marker.SampleOffset >= anchor.StartSampleOffset
                    && marker.SampleOffset < anchor.EndSampleOffset)
                .ToArray();
            foreach (var member in group)
            {
                if (_disabledPartNumbers.Contains(member.Number))
                {
                    continue;
                }

                var memberLength = member.EndSampleOffset - member.StartSampleOffset;
                foreach (var marker in anchorMarkers)
                {
                    var relativeSample = marker.SampleOffset - anchor.StartSampleOffset;
                    if (relativeSample < 0 || relativeSample >= memberLength)
                    {
                        continue;
                    }

                    sharedMarkers.Add(new WaveformMarkerMark(
                        member.StartSampleOffset + relativeSample,
                        marker.Comment,
                        IsSharedProjection: member.Number != anchor.Number,
                        IsFromWaveEmbedded: marker.IsFromWaveEmbedded));
                }
            }
        }

        _effectiveMarkers = sharedMarkers
            .OrderBy(marker => marker.SampleOffset)
            .ToArray();
        _wwiseMarkers = _waveOnlyMarkers is not null ? [] : _effectiveMarkers;
    }

    private bool IsMarkerOnDisabledPart(long sampleOffset)
    {
        if (_disabledPartNumbers.Count == 0)
        {
            return false;
        }

        return Preview.OutputParts.Any(part =>
            _disabledPartNumbers.Contains(part.Number)
            && sampleOffset >= part.StartSampleOffset
            && sampleOffset < part.EndSampleOffset);
    }

    /// <summary>
    /// 追加マーカーが連番・保持の対象か。グループ同期先上の実体は対象外。
    /// </summary>
    private bool IsAuthoritativeUserMarkerSample(long sampleOffset)
    {
        var part = Preview.OutputParts
            .Where(candidate =>
                sampleOffset >= candidate.StartSampleOffset
                && sampleOffset < candidate.EndSampleOffset)
            .Select(candidate => (WaveformOutputPart?)candidate)
            .FirstOrDefault();
        if (part is not { } matchedPart)
        {
            return true;
        }

        if (!_markerShareAnchorByPartNumber.TryGetValue(matchedPart.Number, out var anchor))
        {
            return true;
        }

        return matchedPart.Number == anchor.Number;
    }

    /// <summary>
    /// Reset Per Part 用のパート番号。グループ時は基準 Playlist の index に寄せる。
    /// </summary>
    private int FindNumberingPartIndex(long sampleOffset)
    {
        for (var i = 0; i < Preview.OutputParts.Count; i++)
        {
            var part = Preview.OutputParts[i];
            if (sampleOffset < part.StartSampleOffset || sampleOffset >= part.EndSampleOffset)
            {
                continue;
            }

            if (_markerShareAnchorByPartNumber.TryGetValue(part.Number, out var anchor))
            {
                for (var j = 0; j < Preview.OutputParts.Count; j++)
                {
                    if (Preview.OutputParts[j].Number == anchor.Number)
                    {
                        return j;
                    }
                }
            }

            return i;
        }

        return -1;
    }
}

internal readonly record struct UserWaveformMarker(Guid Id, long SampleOffset);
