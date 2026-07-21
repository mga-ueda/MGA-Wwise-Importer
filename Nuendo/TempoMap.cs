using MgaWwiseIMImporter.UI;

namespace MgaWwiseIMImporter.Nuendo;

internal sealed class TempoMap
{
    private readonly IReadOnlyList<NuendoTempoEvent> _tempoEvents;
    private readonly IReadOnlyList<NuendoSignatureEvent> _signatureEvents;

    public TempoMap(
        IReadOnlyList<NuendoTempoEvent> tempoEvents,
        IReadOnlyList<NuendoSignatureEvent> signatureEvents)
    {
        if (tempoEvents.Count == 0)
        {
            throw new InvalidDataException(UiStrings.ErrNoTempoEvents);
        }

        _tempoEvents = tempoEvents;
        _signatureEvents = signatureEvents;
    }

    public double PpqToSamples(double targetPpq, double sampleRate)
    {
        if (targetPpq <= 0)
        {
            return 0;
        }

        var samples = 0d;
        var first = _tempoEvents[0];
        if (first.Ppq > 0)
        {
            var prefixEnd = Math.Min(first.Ppq, targetPpq);
            samples += ConstantTempoSamples(0, prefixEnd, first.Bpm, sampleRate);
            if (targetPpq <= first.Ppq)
            {
                return samples;
            }
        }

        for (var i = 0; i < _tempoEvents.Count; i++)
        {
            var current = _tempoEvents[i];
            var next = i + 1 < _tempoEvents.Count ? _tempoEvents[i + 1] : null;
            var from = current.Ppq;
            var to = next is null ? targetPpq : Math.Min(next.Ppq, targetPpq);

            if (to > from)
            {
                samples += SegmentSamples(from, to, current, next, sampleRate);
            }

            if (next is null || next.Ppq >= targetPpq)
            {
                break;
            }
        }

        return samples;
    }

    public long PpqToSampleIndex(double targetPpq, double sampleRate)
    {
        return (long)Math.Round(PpqToSamples(targetPpq, sampleRate), MidpointRounding.AwayFromZero);
    }

    public double FindPpqForSamples(long absoluteSample, double sampleRate)
    {
        if (absoluteSample <= 0)
        {
            return 0;
        }

        var lo = 0d;
        var hi = Math.Max(NuendoTracklistInfo.PulsesPerQuarterNote, _tempoEvents[^1].Ppq);
        while (PpqToSamples(hi, sampleRate) < absoluteSample)
        {
            hi *= 2d;
            if (hi > 1_000_000_000d)
            {
                break;
            }
        }

        for (var i = 0; i < 80; i++)
        {
            var mid = (lo + hi) * 0.5d;
            if (PpqToSamples(mid, sampleRate) < absoluteSample)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return hi;
    }

    public double GetBpmAt(double ppq)
    {
        var index = FindEventIndexAtOrBefore(ppq);
        var current = _tempoEvents[index];
        var next = index + 1 < _tempoEvents.Count ? _tempoEvents[index + 1] : null;

        // 次イベントがランプなら、current → next の区間で BPM が連続変化する。
        if (next is null || !next.IsRamp || next.Ppq <= current.Ppq)
        {
            return current.Bpm;
        }

        var t = (ppq - current.Ppq) / (next.Ppq - current.Ppq);
        t = Math.Clamp(t, 0, 1);
        return current.Bpm + (next.Bpm - current.Bpm) * t;
    }

    public NuendoSignatureEvent GetSignatureAt(double ppq)
    {
        if (_signatureEvents.Count == 0)
        {
            return new NuendoSignatureEvent
            {
                Ppq = 0,
                Numerator = 4,
                Denominator = 4,
                Bar = 0,
            };
        }

        var current = _signatureEvents[0];
        foreach (var signature in _signatureEvents)
        {
            if (signature.Ppq > ppq)
            {
                break;
            }

            current = signature;
        }

        return current;
    }

    private int FindEventIndexAtOrBefore(double ppq)
    {
        var index = 0;
        for (var i = 0; i < _tempoEvents.Count; i++)
        {
            if (_tempoEvents[i].Ppq <= ppq)
            {
                index = i;
            }
            else
            {
                break;
            }
        }

        return index;
    }

    private static double SegmentSamples(
        double startPpq,
        double endPpq,
        NuendoTempoEvent current,
        NuendoTempoEvent? next,
        double sampleRate)
    {
        // Func=1 は到着側イベントに付く。区間 [current, next) がランプかどうかは next.IsRamp。
        if (next is not null && next.IsRamp && next.Ppq > current.Ppq)
        {
            return RampTempoSamples(startPpq, endPpq, current, next, sampleRate);
        }

        return ConstantTempoSamples(startPpq, endPpq, current.Bpm, sampleRate);
    }

    private static double ConstantTempoSamples(
        double startPpq,
        double endPpq,
        double bpm,
        double sampleRate)
    {
        var beats = (endPpq - startPpq) / NuendoTracklistInfo.PulsesPerQuarterNote;
        return beats * 60d / bpm * sampleRate;
    }

    private static double RampTempoSamples(
        double startPpq,
        double endPpq,
        NuendoTempoEvent current,
        NuendoTempoEvent next,
        double sampleRate)
    {
        var bpmStart = InterpolateBpm(startPpq, current, next);
        var bpmEnd = InterpolateBpm(endPpq, current, next);
        var beats = (endPpq - startPpq) / NuendoTracklistInfo.PulsesPerQuarterNote;

        double seconds;
        if (Math.Abs(bpmEnd - bpmStart) < 0.000001)
        {
            seconds = beats * 60d / bpmStart;
        }
        else
        {
            seconds = 60d * beats * Math.Log(bpmEnd / bpmStart) / (bpmEnd - bpmStart);
        }

        return seconds * sampleRate;
    }

    private static double InterpolateBpm(double ppq, NuendoTempoEvent current, NuendoTempoEvent next)
    {
        var t = (ppq - current.Ppq) / (next.Ppq - current.Ppq);
        t = Math.Clamp(t, 0, 1);
        return current.Bpm + (next.Bpm - current.Bpm) * t;
    }
}
