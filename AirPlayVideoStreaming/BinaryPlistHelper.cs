using System.Text;

namespace AirPlayVideoStreaming;

public static class BinaryPlistHelper
{
    public static byte[] Write(Dictionary<string, object> dict)
    {
        var ms = new MemoryStream();
        
        // Write header: "bplist00"
        ms.Write(Encoding.ASCII.GetBytes("bplist00"));
        
        // For a simple implementation, we'll create a minimal binary plist
        // This is a simplified version - a full implementation would be more complex
        
        // Object table - dictionary with count
        var objectRefs = new List<int>();
        int objectId = 0;
        
        // Write dictionary header (0xD0 = dict, followed by count as single byte if < 15)
        int count = dict.Count;
        if (count < 15)
        {
            ms.WriteByte((byte)(0xD0 | count));
        }
        else
        {
            ms.WriteByte(0xD0);
            ms.WriteByte((byte)count);
        }
        
        // Write key-value pairs
        foreach (var kvp in dict)
        {
            // Write key (string)
            WriteString(ms, kvp.Key, ref objectId);
            objectRefs.Add(objectId++);
            
            // Write value
            WriteValue(ms, kvp.Value, ref objectId);
            objectRefs.Add(objectId++);
        }
        
        // Offset table (simplified - just write offsets)
        var offsetTableStart = ms.Position;
        foreach (var offset in objectRefs)
        {
            WriteInt(ms, offset, 1); // 1 byte offsets for simplicity
        }
        
        // Trailer
        var trailerStart = ms.Position;
        WriteInt(ms, 0, 1); // offset size
        WriteInt(ms, 1, 1); // ref size
        WriteLong(ms, objectRefs.Count, 8); // object count
        WriteLong(ms, 0, 8); // top object (dictionary)
        WriteLong(ms, offsetTableStart, 8); // offset table offset
        
        return ms.ToArray();
    }
    
    private static void WriteString(MemoryStream ms, string str, ref int objectId)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        if (bytes.Length < 15)
        {
            ms.WriteByte((byte)(0x50 | bytes.Length)); // ASCII string
        }
        else
        {
            ms.WriteByte(0x50);
            WriteInt(ms, bytes.Length, 1);
        }
        ms.Write(bytes);
    }
    
    private static void WriteValue(MemoryStream ms, object value, ref int objectId)
    {
        switch (value)
        {
            case string str:
                WriteString(ms, str, ref objectId);
                break;
            case double d:
                ms.WriteByte(0x23); // real
                ms.Write(BitConverter.GetBytes(d).Reverse().ToArray());
                break;
            case float f:
                ms.WriteByte(0x22); // real (float)
                ms.Write(BitConverter.GetBytes(f).Reverse().ToArray());
                break;
            case int i:
                if (i >= 0 && i < 15)
                {
                    ms.WriteByte((byte)(0x10 | i)); // integer
                }
                else
                {
                    ms.WriteByte(0x13); // int64
                    WriteLong(ms, i, 8);
                }
                break;
            default:
                WriteString(ms, value.ToString() ?? "", ref objectId);
                break;
        }
    }
    
    private static void WriteInt(MemoryStream ms, int value, int bytes)
    {
        for (int i = bytes - 1; i >= 0; i--)
        {
            ms.WriteByte((byte)((value >> (i * 8)) & 0xFF));
        }
    }
    
    private static void WriteLong(MemoryStream ms, long value, int bytes)
    {
        for (int i = bytes - 1; i >= 0; i--)
        {
            ms.WriteByte((byte)((value >> (i * 8)) & 0xFF));
        }
    }
    
    public static object? Read(byte[] data)
    {
        // Simple binary plist reader - for now just return the raw data as string representation
        // A full implementation would parse the binary plist structure
        if (data.Length < 8 || Encoding.ASCII.GetString(data, 0, 8) != "bplist00")
        {
            return null;
        }
        
        // For now, return a simple representation
        return $"Binary plist ({data.Length} bytes)";
    }
}

