using System.Text;

namespace MgaWwiseImporter.Wave;

/// <summary>
/// WAV の cue / LIST adtl チャンクを書き出す。
/// </summary>
internal static class WavCueWriter
{
    // 日本語 Windows などでは系統 ANSI（ここでは CP932）で書くエディタが多い。
    private static Encoding TextEncoding => Encoding.GetEncoding(932);

    public static void Write(string sourcePath, string destinationPath, IReadOnlyList<WavCueItem> cues)
    {
        var chunks = ReadChunks(sourcePath);
        var retained = chunks
            .Where(chunk => !IsCueRelated(chunk.Id, chunk.Data))
            .ToList();

        var dataIndex = retained.FindIndex(chunk => chunk.Id == "data");
        if (dataIndex < 0)
        {
            throw new InvalidDataException("data チャンクが見つかりません。");
        }

        retained.Insert(dataIndex, BuildCueChunk(cues));
        retained.Insert(dataIndex + 1, BuildAdtlListChunk(cues));

        WriteWaveFile(destinationPath, retained);
    }

    private static bool IsCueRelated(string id, byte[] data)
    {
        if (id is "cue " or "smpl")
        {
            return true;
        }

        if (id == "LIST" && data.Length >= 4)
        {
            var listType = Encoding.ASCII.GetString(data, 0, 4);
            return listType is "adtl";
        }

        return false;
    }

    private static List<RiffChunk> ReadChunks(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        var riff = ReadFourCc(reader);
        if (riff != "RIFF")
        {
            throw new InvalidDataException("RIFF ヘッダーではありません。");
        }

        _ = reader.ReadUInt32();
        var wave = ReadFourCc(reader);
        if (wave != "WAVE")
        {
            throw new InvalidDataException("WAVE 形式ではありません。");
        }

        var chunks = new List<RiffChunk>();
        while (stream.Position + 8 <= stream.Length)
        {
            var id = ReadFourCc(reader);
            var size = reader.ReadInt32();
            if (size < 0 || stream.Position + size > stream.Length)
            {
                throw new InvalidDataException($"チャンクサイズが不正です: {id}");
            }

            var data = reader.ReadBytes(size);
            if ((size & 1) == 1 && stream.Position < stream.Length)
            {
                _ = reader.ReadByte();
            }

            chunks.Add(new RiffChunk(id, data));
        }

        return chunks;
    }

    private static void WriteWaveFile(string path, IReadOnlyList<RiffChunk> chunks)
    {
        var contentSize = 4;
        foreach (var chunk in chunks)
        {
            contentSize += 8 + chunk.Data.Length + (chunk.Data.Length & 1);
        }

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(contentSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        foreach (var chunk in chunks)
        {
            writer.Write(Encoding.ASCII.GetBytes(chunk.Id));
            writer.Write(chunk.Data.Length);
            writer.Write(chunk.Data);
            if ((chunk.Data.Length & 1) == 1)
            {
                writer.Write((byte)0);
            }
        }
    }

    private static RiffChunk BuildCueChunk(IReadOnlyList<WavCueItem> cues)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(cues.Count);
            foreach (var cue in cues)
            {
                writer.Write(cue.Id);
                writer.Write(checked((uint)cue.SampleOffset));
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(0);
                writer.Write(0);
                writer.Write(checked((uint)cue.SampleOffset));
            }
        }

        return new RiffChunk("cue ", stream.ToArray());
    }

    private static RiffChunk BuildAdtlListChunk(IReadOnlyList<WavCueItem> cues)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("adtl"));

            foreach (var cue in cues)
            {
                // リージョン: 表示名は Name(labl) に出す（多くのエディタはこちらを見せる）。
                // マーカー: Name はマーカー名、テンポ/拍子は note に入れる。
                var label = cue.IsRegion
                    ? BuildRegionLabel(cue.Name, cue.Comment)
                    : cue.Name;

                WriteAdtlTextChunk(writer, "labl", cue.Id, label);

                if (cue.IsRegion)
                {
                    WriteLtxtChunk(writer, cue.Id, cue.SampleLength, cue.Comment);
                }
                else
                {
                    WriteAdtlTextChunk(writer, "note", cue.Id, cue.Comment);
                }
            }
        }

        return new RiffChunk("LIST", stream.ToArray());
    }

    private static string BuildRegionLabel(string name, string comment)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return comment;
        }

        if (string.IsNullOrWhiteSpace(comment))
        {
            return name;
        }

        return $"{comment} {name}";
    }

    private static void WriteAdtlTextChunk(BinaryWriter writer, string chunkId, uint cueId, string text)
    {
        var textBytes = TextEncoding.GetBytes(text ?? string.Empty);
        var payloadLength = 4 + textBytes.Length + 1;
        writer.Write(Encoding.ASCII.GetBytes(chunkId));
        writer.Write(payloadLength);
        writer.Write(cueId);
        writer.Write(textBytes);
        writer.Write((byte)0);
        WritePadIfOdd(writer, payloadLength);
    }

    private static void WriteLtxtChunk(
        BinaryWriter writer,
        uint cueId,
        long sampleLength,
        string text)
    {
        // dwName + dwSampleLength + dwPurpose + country/language/dialect/codepage + text
        var textBytes = TextEncoding.GetBytes(text ?? string.Empty);
        var payloadLength = 20 + textBytes.Length + 1;
        writer.Write(Encoding.ASCII.GetBytes("ltxt"));
        writer.Write(payloadLength);
        writer.Write(cueId);
        writer.Write(checked((uint)sampleLength));
        writer.Write(Encoding.ASCII.GetBytes("rgn "));
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(textBytes);
        writer.Write((byte)0);
        WritePadIfOdd(writer, payloadLength);
    }

    private static void WritePadIfOdd(BinaryWriter writer, int payloadLength)
    {
        if ((payloadLength & 1) == 1)
        {
            writer.Write((byte)0);
        }
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(4));
    }

    private sealed class RiffChunk(string id, byte[] data)
    {
        public string Id { get; } = id;
        public byte[] Data { get; } = data;
    }
}
