using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace MgaWwiseImporter.Nuendo;

internal enum NuendoMarkerKind
{
    Marker,
    CycleRegion,
}

internal sealed class NuendoTempoEvent
{
    public double Bpm { get; init; }
    public double Ppq { get; init; }
    public int? Func { get; init; }

    public double QuarterNotes => Ppq / NuendoTracklistInfo.PulsesPerQuarterNote;

    /// <summary>
    /// Nuendo: Func=1 は「直前イベントの BPM からこのイベントの BPM へ」直線変化。
    /// </summary>
    public bool IsRamp => Func is 1;
}

internal sealed class NuendoSignatureEvent
{
    public double Ppq { get; init; }
    public int Numerator { get; init; }
    public int Denominator { get; init; }
    public int Bar { get; init; }
}

internal sealed class NuendoMarkerEvent
{
    public NuendoMarkerKind Kind { get; init; }
    public double StartPpq { get; init; }
    public double LengthPpq { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Id { get; init; }

    public double EndPpq => StartPpq + LengthPpq;
}

internal sealed class NuendoTracklistInfo
{
    /// <summary>Cubase / Nuendo のテンポ・拍子イベントで一般的な PPQ 分解能。</summary>
    public const double PulsesPerQuarterNote = 480d;

    public required string Path { get; init; }
    public double? RehearsalTempo { get; init; }
    public IReadOnlyList<NuendoTempoEvent> TempoEvents { get; init; } = [];
    public IReadOnlyList<NuendoSignatureEvent> SignatureEvents { get; init; } = [];
    public IReadOnlyList<NuendoMarkerEvent> MarkerEvents { get; init; } = [];

    public static NuendoTracklistInfo Read(string path)
    {
        var document = XDocument.Load(path);

        var tempoTrack = document
            .Descendants("obj")
            .FirstOrDefault(e => (string?)e.Attribute("class") == "MTempoTrackEvent")
            ?? throw new InvalidDataException("MTempoTrackEvent (Tempo Track) が見つかりません。");

        var signatureTrack = document
            .Descendants("obj")
            .FirstOrDefault(e => (string?)e.Attribute("class") == "MSignatureTrackEvent");

        var markerTrack = document
            .Descendants("obj")
            .FirstOrDefault(e => (string?)e.Attribute("class") == "MMarkerTrackEvent");

        var tempoEvents = tempoTrack
            .Elements("list")
            .Where(list => (string?)list.Attribute("name") == "TempoEvent")
            .SelectMany(list => list.Elements("obj"))
            .Where(obj => (string?)obj.Attribute("class") == "MTempoEvent")
            .Select(obj => new NuendoTempoEvent
            {
                Bpm = NuendoXml.ReadFloatChild(obj, "BPM")
                    ?? throw new InvalidDataException("TempoEvent に BPM がありません。"),
                Ppq = NuendoXml.ReadFloatChild(obj, "PPQ")
                    ?? throw new InvalidDataException("TempoEvent に PPQ がありません。"),
                Func = NuendoXml.ReadIntChild(obj, "Func"),
            })
            .OrderBy(e => e.Ppq)
            .ToList();

        var signatureEvents = signatureTrack is null
            ? []
            : signatureTrack
                .Elements("list")
                .Where(list => (string?)list.Attribute("name") == "SignatureEvent")
                .SelectMany(list => list.Elements("obj"))
                .Where(obj => (string?)obj.Attribute("class") == "MTimeSignatureEvent")
                .Select(obj => new NuendoSignatureEvent
                {
                    Ppq = NuendoXml.ReadNumberChild(obj, "Position") ?? 0,
                    Numerator = NuendoXml.ReadIntChild(obj, "Numerator") ?? 4,
                    Denominator = NuendoXml.ReadIntChild(obj, "Denominator") ?? 4,
                    Bar = NuendoXml.ReadIntChild(obj, "Bar") ?? 0,
                })
                .OrderBy(e => e.Ppq)
                .ToList();

        var markerEvents = markerTrack is null
            ? []
            : markerTrack
                .Descendants("list")
                .Where(list => (string?)list.Attribute("name") == "Events")
                .SelectMany(list => list.Elements("obj"))
                .Select(ParseMarker)
                .Where(marker => marker is not null)
                .Select(marker => marker!)
                .OrderBy(m => m.StartPpq)
                .ThenBy(m => m.Kind)
                .ToList();

        return new NuendoTracklistInfo
        {
            Path = path,
            RehearsalTempo = NuendoXml.ReadFloatChild(tempoTrack, "RehearsalTempo"),
            TempoEvents = tempoEvents,
            SignatureEvents = signatureEvents,
            MarkerEvents = markerEvents,
        };
    }

    public string ToDisplayText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Nuendo Tempo Track ===");
        sb.AppendLine($"Path            : {Path}");
        sb.AppendLine($"Rehearsal Tempo : {FormatOptional(RehearsalTempo, "BPM")}");
        sb.AppendLine($"PPQ Resolution  : {PulsesPerQuarterNote:0} pulses / quarter note");
        sb.AppendLine($"Tempo Events    : {TempoEvents.Count}");
        sb.AppendLine($"Signatures      : {SignatureEvents.Count}");
        sb.AppendLine($"Markers         : {MarkerEvents.Count}");
        sb.AppendLine();

        for (var i = 0; i < TempoEvents.Count; i++)
        {
            var tempoEvent = TempoEvents[i];
            var funcText = tempoEvent.Func is null
                ? "-"
                : tempoEvent.Func.Value.ToString(CultureInfo.InvariantCulture);
            sb.AppendLine(
                $"Tempo[{i}] PPQ={FormatNumber(tempoEvent.Ppq)}"
                + $"  Beat={FormatNumber(tempoEvent.QuarterNotes)}"
                + $"  BPM={FormatNumber(tempoEvent.Bpm)}"
                + $"  Func={funcText}");
        }

        sb.AppendLine();
        for (var i = 0; i < SignatureEvents.Count; i++)
        {
            var signature = SignatureEvents[i];
            sb.AppendLine(
                $"Sig[{i}] PPQ={FormatNumber(signature.Ppq)}"
                + $"  Bar={signature.Bar}"
                + $"  {signature.Numerator}/{signature.Denominator}");
        }

        sb.AppendLine();
        for (var i = 0; i < MarkerEvents.Count; i++)
        {
            var marker = MarkerEvents[i];
            var kind = marker.Kind == NuendoMarkerKind.CycleRegion ? "Region" : "Marker";
            sb.AppendLine(
                $"{kind}[{i}] PPQ={FormatNumber(marker.StartPpq)}"
                + (marker.Kind == NuendoMarkerKind.CycleRegion
                    ? $"  Len={FormatNumber(marker.LengthPpq)}"
                    : string.Empty)
                + $"  Name=\"{marker.Name}\"");
        }

        return sb.ToString();
    }

    private static NuendoMarkerEvent? ParseMarker(XElement obj)
    {
        var className = (string?)obj.Attribute("class");
        NuendoMarkerKind kind;
        if (className == "MRangeMarkerEvent")
        {
            kind = NuendoMarkerKind.CycleRegion;
        }
        else if (className == "MMarkerEvent")
        {
            kind = NuendoMarkerKind.Marker;
        }
        else
        {
            return null;
        }

        return new NuendoMarkerEvent
        {
            Kind = kind,
            StartPpq = NuendoXml.ReadFloatChild(obj, "Start") ?? 0,
            LengthPpq = NuendoXml.ReadFloatChild(obj, "Length") ?? 0,
            Name = NuendoXml.ReadStringChild(obj, "Name"),
            Id = NuendoXml.ReadIntChild(obj, "ID") ?? 0,
        };
    }

    private static string FormatOptional(double? value, string unit)
    {
        return value is null ? "-" : $"{FormatNumber(value.Value)} {unit}";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
