using System.Buffers.Binary;

namespace HorrorGame;

internal static class FontLoader
{
    // Windows の TTC コレクションから先頭フォントだけをメモリ上に構築。
    // 元のシステムフォントを変更・コピー配置せずに利用する。
    public static byte[] ReadTrueType(string path)
    {
        byte[] collection = File.ReadAllBytes(path);
        if (collection.Length < 16 || collection[0] != 't' || collection[1] != 't'
            || collection[2] != 'c' || collection[3] != 'f') return collection;
        int header = checked((int)BinaryPrimitives.ReadUInt32BigEndian(collection.AsSpan(12, 4)));
        int count = BinaryPrimitives.ReadUInt16BigEndian(collection.AsSpan(header + 4, 2));
        int directorySize = checked(12 + count * 16);
        using var output = new MemoryStream();
        output.Write(collection, header, directorySize);
        for (int i = 0; i < count; i++)
        {
            int record = header + 12 + i * 16;
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(collection.AsSpan(record + 8, 4)));
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(collection.AsSpan(record + 12, 4)));
            uint destination = checked((uint)output.Length);
            output.Position = output.Length;
            output.Write(collection, offset, length);
            while (output.Length % 4 != 0) output.WriteByte(0);
            output.Position = 12 + i * 16 + 8;
            byte[] address = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(address, destination);
            output.Write(address);
        }
        return output.ToArray();
    }
}

