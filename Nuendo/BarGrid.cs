namespace MgaWwiseImporter.Nuendo;

internal static class BarGrid
{
    /// <summary>
    /// N/D 拍子の1小節長 (PPQ)。1拍 = 4/D 四分音符、1小節 = N 拍。
    /// </summary>
    public static double BarLengthPpq(int numerator, int denominator)
    {
        if (numerator <= 0 || denominator <= 0)
        {
            return NuendoTracklistInfo.PulsesPerQuarterNote * 4d;
        }

        return NuendoTracklistInfo.PulsesPerQuarterNote * numerator * 4d / denominator;
    }

    /// <summary>
    /// untilPpq までの小節線と、それを超える直後の小節線を返す。
    /// </summary>
    public static IReadOnlyList<double> GetBarBoundaries(
        IReadOnlyList<NuendoSignatureEvent> signatures,
        double untilPpq)
    {
        var bounds = new SortedSet<double> { 0d };
        if (untilPpq < 0)
        {
            return bounds.ToList();
        }

        if (signatures.Count == 0)
        {
            AddBarsThrough(bounds, startPpq: 0d, untilPpq: untilPpq, numerator: 4, denominator: 4);
            return bounds.ToList();
        }

        for (var i = 0; i < signatures.Count; i++)
        {
            var signature = signatures[i];
            bounds.Add(signature.Ppq);

            var segmentLimit = i + 1 < signatures.Count
                ? signatures[i + 1].Ppq
                : untilPpq;

            AddBarsThrough(
                bounds,
                startPpq: signature.Ppq,
                untilPpq: segmentLimit,
                numerator: signature.Numerator,
                denominator: signature.Denominator);
        }

        var lastSignature = signatures[0];
        for (var i = signatures.Count - 1; i >= 0; i--)
        {
            if (signatures[i].Ppq <= untilPpq)
            {
                lastSignature = signatures[i];
                break;
            }
        }

        AddBarsThrough(
            bounds,
            startPpq: lastSignature.Ppq,
            untilPpq: untilPpq,
            numerator: lastSignature.Numerator,
            denominator: lastSignature.Denominator);

        return bounds.ToList();
    }

    public static double? FindPreviousBarPpq(IReadOnlyList<double> barBoundaries, double ppq)
    {
        double? previous = null;
        foreach (var barPpq in barBoundaries)
        {
            if (barPpq > ppq + 1e-9)
            {
                break;
            }

            previous = barPpq;
        }

        return previous;
    }

    public static double? FindNextBarPpq(IReadOnlyList<double> barBoundaries, double ppq)
    {
        foreach (var barPpq in barBoundaries)
        {
            if (barPpq > ppq + 1e-9)
            {
                return barPpq;
            }
        }

        return null;
    }

    private static void AddBarsThrough(
        SortedSet<double> bounds,
        double startPpq,
        double untilPpq,
        int numerator,
        int denominator)
    {
        var barLength = BarLengthPpq(numerator, denominator);
        if (barLength <= 0)
        {
            return;
        }

        for (var barIndex = 0; ; barIndex++)
        {
            var ppq = startPpq + (barIndex * barLength);
            bounds.Add(ppq);
            if (ppq > untilPpq + 1e-9)
            {
                break;
            }

            if (barIndex > 1_000_000)
            {
                break;
            }
        }
    }
}
