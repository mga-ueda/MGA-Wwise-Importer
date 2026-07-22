using System.Text;
using MgaWwiseIMImporter.UI;

namespace MgaWwiseIMImporter.Wave;

/// <summary>
/// XML なし・複数 WAV を仮想タイムラインへ連結する（単体 Wave／XML 経路には混ぜない）。
/// ファイル単位のマーカー／smpl／リージョンは <see cref="WaveOnlyModeProcessor"/> を再利用する。
/// </summary>
internal static class MultiWaveOnlyProcessor
{
    private const int OverviewPeakCount = 2400;

    public static WaveformPreviewData? TryBuild(
        IReadOnlyList<string> wavPaths,
        StringBuilder sb)
    {
        if (wavPaths.Count < 2)
        {
            return null;
        }

        sb.AppendLine(UiStrings.LogMultiWaveOnlyHeader);
        sb.AppendLine(UiStrings.LogMultiWaveOnlyModeName(wavPaths.Count));
        sb.AppendLine();

        var infos = new List<WavFileInfo>(wavPaths.Count);
        foreach (var wavPath in wavPaths)
        {
            if (!File.Exists(wavPath))
            {
                sb.AppendLine(UiStrings.LogErrorHeader);
                sb.AppendLine(UiStrings.LogWaveMissing(wavPath));
                sb.AppendLine();
                return null;
            }

            try
            {
                var info = WavFileInfo.Read(wavPath);
                infos.Add(info);
                sb.AppendLine(info.ToDisplayText());
            }
            catch (Exception ex)
            {
                sb.AppendLine(UiStrings.LogErrorHeader);
                sb.AppendLine($"{UiStrings.KeyPath} {wavPath}");
                sb.AppendLine($"{UiStrings.KeyMessage} {ex.Message}");
                sb.AppendLine();
                return null;
            }
        }

        if (!TryValidateFormats(infos, sb, out var reference))
        {
            return null;
        }

        long totalFrames = 0;
        foreach (var info in infos)
        {
            if (info.FrameCount <= 0)
            {
                sb.AppendLine(UiStrings.LogErrorHeader);
                sb.AppendLine($"{UiStrings.KeyPath} {info.Path}");
                sb.AppendLine(UiStrings.LogMultiWaveOnlyEmptyWave);
                sb.AppendLine();
                return null;
            }

            totalFrames = checked(totalFrames + info.FrameCount);
        }

        var peakAllocations = AllocatePeakBuckets(OverviewPeakCount, infos);
        var spans = new List<WaveformSourceSpan>(infos.Count);
        var allMarkers = new List<WaveformMarkerMark>();
        var peakBuckets = new List<WavPeakData>(infos.Count);
        long virtualOffset = 0;

        for (var i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            var wavPath = info.Path;
            var frameCount = info.FrameCount;

            spans.Add(new WaveformSourceSpan(wavPath, info, virtualOffset, frameCount));

            sb.AppendLine(UiStrings.LogMultiWaveOnlyFileHeader(i + 1, wavPaths.Count, wavPath));

            IReadOnlyList<WaveformMarkerMark> markers = [];
            IReadOnlyList<WaveformRegionMark> regions = [];
            IReadOnlyList<WaveformOutputPart> localParts = [];

            try
            {
                var embedded = WavEmbeddedMarkerInfo.Read(wavPath);
                var waveOnlyMode = WaveOnlyModeProcessor.Resolve(embedded);
                sb.AppendLine(UiStrings.LogWaveOnlyModeName(waveOnlyMode));

                if (waveOnlyMode == WaveOnlyMode.MarkersOnly
                    || waveOnlyMode == WaveOnlyMode.SmplLoop)
                {
                    var materializeRenames = new List<WaveOnlyModeProcessor.MarkerCommentRename>();

                    if (waveOnlyMode == WaveOnlyMode.MarkersOnly)
                    {
                        markers = WaveOnlyModeProcessor.BuildMarkersOnly(embedded, frameCount);
                        var sessionMarkers = markers.ToList();
                        WaveOnlyModeProcessor.TryMaterializeImplicitLoopComments(
                            sessionMarkers,
                            frameCount,
                            renames: materializeRenames);
                        markers = sessionMarkers;
                        sb.AppendLine(UiStrings.LogWaveOnlyMarkersOnlySummary(markers.Count));
                    }
                    else
                    {
                        var smplBuild = WaveOnlyModeProcessor.BuildMarkersFromSmplLoops(
                            embedded,
                            frameCount);
                        markers = smplBuild.Markers;
                        sb.AppendLine(
                            UiStrings.LogWaveOnlySmplLoopSummary(
                                smplBuild.AcceptedLoopCount,
                                smplBuild.SkippedLoopCount));
                        AppendDiscardedEmbeddedMarks(sb, smplBuild.DiscardedMarks);
                    }

                    regions = WaveOnlyModeProcessor.BuildRegionsFromMarkers(markers, frameCount);
                    if (regions.Count > 0)
                    {
                        localParts = WaveformRegionBuilder.BuildOutputParts(regions, wavPath);
                    }

                    foreach (var rename in materializeRenames)
                    {
                        sb.AppendLine(
                            UiStrings.LogWaveOnlyMarkerRenamed(
                                rename.FromComment,
                                rename.ToComment));
                    }

                    AppendWaveOnlyRegionSummary(sb, markers, localParts.Count);
                }
                else
                {
                    sb.AppendLine(UiStrings.LogWaveOnlyModeNotImplemented);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine(UiStrings.LogErrorHeader);
                sb.AppendLine($"{UiStrings.KeyPath} {wavPath}");
                sb.AppendLine($"{UiStrings.KeyMessage} {ex.Message}");
                sb.AppendLine();
                return null;
            }

            foreach (var marker in markers)
            {
                allMarkers.Add(marker with
                {
                    SampleOffset = marker.SampleOffset + virtualOffset,
                });
            }

            // ログ用にローカルパート数を記録（最終パートは境界付きで再構築）
            peakBuckets.Add(WavPeakReader.Read(info, peakAllocations[i]));

            sb.AppendLine(
                UiStrings.LogMultiWaveOnlySpanSummary(
                    virtualOffset,
                    virtualOffset + frameCount,
                    localParts.Count));
            sb.AppendLine();

            virtualOffset += frameCount;
        }

        // ファイル境界でパートを分断し、1 ファイル＝1 Playlist 系統にする
        var built = MultiWaveOnlyRegionBuilder.Build(allMarkers, spans);
        var allRegions = built.Regions;
        var allParts = built.Parts;

        var peaks = WavPeakReader.Concatenate(peakBuckets, totalFrames, reference.SampleRate);
        var virtualInfo = CreateVirtualWavInfo(reference, totalFrames, wavPaths[0]);

        sb.AppendLine(UiStrings.LogWaveformHeader);
        sb.AppendLine($"{UiStrings.KeySource} {UiStrings.LogMultiWaveOnlyVirtualSource(wavPaths.Count)}");
        sb.AppendLine(UiStrings.LogPeaksSummary(peaks.Mins.Length, peaks.FrameCount));
        sb.AppendLine($"{UiStrings.KeyRegions} {allRegions.Count}");
        sb.AppendLine($"{UiStrings.KeyOutputs} {allParts.Count}");
        foreach (var part in allParts)
        {
            sb.AppendLine(
                $"  - {part.FileName}"
                + $"  samples=[{part.StartSampleOffset:N0} .. {part.EndSampleOffset:N0})"
                + $"  src={Path.GetFileName(part.SourcePath)}");
        }

        sb.AppendLine();

        return new WaveformPreviewData(
            peaks,
            wavPaths[0],
            virtualInfo,
            bars: [],
            markers: allMarkers,
            cycles: [],
            regions: allRegions,
            outputParts: allParts,
            allowsSessionMarkerEdit: true,
            sourceSpans: spans);
    }

    private static bool TryValidateFormats(
        IReadOnlyList<WavFileInfo> infos,
        StringBuilder sb,
        out WavFileInfo reference)
    {
        reference = infos[0];
        for (var i = 1; i < infos.Count; i++)
        {
            var other = infos[i];
            if (other.SampleRate != reference.SampleRate
                || other.Channels != reference.Channels
                || other.BitsPerSample != reference.BitsPerSample
                || other.AudioFormat != reference.AudioFormat
                || other.BlockAlign != reference.BlockAlign)
            {
                sb.AppendLine(UiStrings.LogErrorHeader);
                sb.AppendLine(UiStrings.LogMultiWaveOnlyFormatMismatch(reference.Path, other.Path));
                sb.AppendLine(
                    UiStrings.LogMultiWaveOnlyFormatDetail(
                        reference.SampleRate,
                        reference.Channels,
                        reference.BitsPerSample,
                        other.SampleRate,
                        other.Channels,
                        other.BitsPerSample));
                sb.AppendLine();
                return false;
            }
        }

        return true;
    }

    /// <summary>概要ピークバケットをフレーム数比で割り当て（合計 ≈ totalBuckets）。</summary>
    private static int[] AllocatePeakBuckets(int totalBuckets, IReadOnlyList<WavFileInfo> infos)
    {
        var result = new int[infos.Count];
        long totalFrames = 0;
        foreach (var info in infos)
        {
            totalFrames += info.FrameCount;
        }

        if (totalFrames <= 0)
        {
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = 1;
            }

            return result;
        }

        var assigned = 0;
        for (var i = 0; i < infos.Count; i++)
        {
            if (i == infos.Count - 1)
            {
                result[i] = Math.Max(1, totalBuckets - assigned);
            }
            else
            {
                var share = (int)Math.Round(totalBuckets * (infos[i].FrameCount / (double)totalFrames));
                result[i] = Math.Max(1, share);
                assigned += result[i];
            }
        }

        // 丸め誤差で合計が totalBuckets を大きく超えた場合、末尾以外を 1 まで削る
        var sum = result.Sum();
        if (sum > totalBuckets)
        {
            var excess = sum - totalBuckets;
            for (var i = 0; i < result.Length - 1 && excess > 0; i++)
            {
                var canCut = result[i] - 1;
                if (canCut <= 0)
                {
                    continue;
                }

                var cut = Math.Min(canCut, excess);
                result[i] -= cut;
                excess -= cut;
            }

            result[^1] = Math.Max(1, result[^1] - excess);
        }

        return result;
    }

    internal static WavFileInfo CreateVirtualWavInfo(
        WavFileInfo template,
        long totalFrames,
        string displayPath)
    {
        var dataBytes = checked(totalFrames * template.BlockAlign);
        if (dataBytes > uint.MaxValue)
        {
            throw new InvalidDataException(UiStrings.ErrMultiWaveOnlyTooLong);
        }

        return new WavFileInfo
        {
            Path = displayPath,
            FileSizeBytes = dataBytes,
            AudioFormat = template.AudioFormat,
            Channels = template.Channels,
            SampleRate = template.SampleRate,
            ByteRate = template.ByteRate,
            BlockAlign = template.BlockAlign,
            BitsPerSample = template.BitsPerSample,
            DataSizeBytes = (uint)dataBytes,
            HasIXml = false,
            TimeReferenceSamples = 0,
        };
    }

    private static void AppendWaveOnlyRegionSummary(
        StringBuilder sb,
        IReadOnlyList<WaveformMarkerMark> markers,
        int outputPartCount)
    {
        var loopRegionCount = markers.Count == 2
            ? 1
            : markers.Count(marker =>
                WaveOnlyModeProcessor.IsLoopRelatedComment(marker.Comment));
        if (loopRegionCount > 0)
        {
            sb.AppendLine(UiStrings.LogWaveOnlyLoopRegions(loopRegionCount));
        }

        var removeRegionCount = markers.Count(marker =>
            WaveOnlyModeProcessor.IsRemoveOnlyComment(marker.Comment));
        if (removeRegionCount > 0)
        {
            sb.AppendLine(UiStrings.LogWaveOnlyRemoveRegions(removeRegionCount));
        }

        var exitRegionCount = markers.Count(marker =>
            WaveOnlyModeProcessor.IsExitOnlyComment(marker.Comment));
        if (exitRegionCount > 0)
        {
            sb.AppendLine(UiStrings.LogWaveOnlyExitRegions(exitRegionCount));
        }

        var anacrusisRegionCount = markers.Count(marker =>
            WaveOnlyModeProcessor.IsAnacrusisOnlyComment(marker.Comment));
        if (anacrusisRegionCount > 0)
        {
            sb.AppendLine(UiStrings.LogWaveOnlyAnacrusisRegions(anacrusisRegionCount));
        }

        if (outputPartCount > 0)
        {
            sb.AppendLine(UiStrings.LogWaveOnlyOutputParts(outputPartCount));
        }
    }

    private static void AppendDiscardedEmbeddedMarks(
        StringBuilder sb,
        IReadOnlyList<WaveOnlyModeProcessor.DiscardedEmbeddedMark> discardedMarks)
    {
        if (discardedMarks.Count == 0)
        {
            return;
        }

        sb.AppendLine(UiStrings.LogWaveOnlyDiscardedEmbeddedSummary(discardedMarks.Count));
        foreach (var mark in discardedMarks)
        {
            sb.AppendLine(
                UiStrings.LogWaveOnlyDiscardedEmbeddedItem(
                    mark.Kind,
                    mark.SampleOffset,
                    mark.Comment));
        }
    }
}
