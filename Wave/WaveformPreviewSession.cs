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
}

/// <summary>
/// 読み込み元の不変プレビューと、現在の読み込み中だけ有効な追加マーカーを管理する。
/// </summary>
internal sealed class WaveformPreviewSession
{
    private readonly List<UserWaveformMarker> _userMarkers = [];
    private readonly Dictionary<int, WaveformOutputPart> _markerShareAnchorByPartNumber = [];
    private readonly List<IReadOnlyList<WaveformOutputPart>> _markerShareGroups = [];
    private HashSet<int> _disabledPartNumbers = [];
    private IReadOnlyList<WaveformMarkerMark> _effectiveMarkers;
    private IReadOnlyList<WaveformMarkerMark> _wwiseMarkers;
    private MarkerCommentRule _commentRule = MarkerCommentRule.Default;

    public WaveformPreviewSession(WaveformPreviewData preview)
    {
        Preview = preview;
        _effectiveMarkers = preview.Markers;
        _wwiseMarkers = preview.Markers;
    }

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

            _userMarkers.Add(new UserWaveformMarker(Guid.NewGuid(), sampleOffset));
            changed = true;
        }

        if (changed)
        {
            RebuildMarkerSnapshots();
        }

        return changed;
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

        var baseMarkers = Preview.Markers
            .Concat(userMarkerMarks)
            .Where(marker => !IsMarkerOnDisabledPart(marker.SampleOffset))
            .OrderBy(marker => marker.SampleOffset)
            .ToArray();

        if (_markerShareGroups.Count == 0)
        {
            _effectiveMarkers = baseMarkers;
            _wwiseMarkers = _effectiveMarkers;
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
                        IsSharedProjection: member.Number != anchor.Number));
                }
            }
        }

        _effectiveMarkers = sharedMarkers
            .OrderBy(marker => marker.SampleOffset)
            .ToArray();
        _wwiseMarkers = _effectiveMarkers;
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
