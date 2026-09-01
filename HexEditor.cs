// HexEditor.cs
// Версия на C# с использованием MemoryMappedFile, Span<byte>, CRC32

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Collections.Generic;

public class HexEditor : IDisposable
{
    private readonly string filename;
    private readonly int cols;
    private readonly MemoryMappedFile mmf;
    private readonly MemoryMappedViewAccessor accessor;
    private readonly long size;
    private byte[] data; // для простоты загружаем весь файл (для малых файлов)

    public HexEditor(string filename, int cols = 16)
    {
        this.filename = filename;
        this.cols = cols;
        this.size = new FileInfo(filename).Length;
        this.mmf = MemoryMappedFile.CreateFromFile(filename, FileMode.Open, null, 0, MemoryMappedFileAccess.ReadWrite);
        this.accessor = mmf.CreateViewAccessor(0, size, MemoryMappedFileAccess.ReadWrite);
        // Читаем данные в массив для простоты (для больших файлов лучше использовать потоки)
        this.data = new byte[size];
        this.accessor.ReadArray(0, data, 0, (int)size);
    }

    public void View(long offset, long length = 0)
    {
        if (length == 0) length = size - offset;
        if (offset < 0 || offset + length > size) throw new ArgumentOutOfRangeException();
        var chunk = data.Skip((int)offset).Take((int)length).ToArray();
        Dump(chunk, offset);
    }

    private void Dump(byte[] data, long baseOffset)
    {
        for (int i = 0; i < data.Length; i += cols)
        {
            int end = Math.Min(i + cols, data.Length);
            long addr = baseOffset + i;
            Console.Write($"{Colorize($"{addr:X8}", ConsoleColor.Cyan)}  ");
            // HEX
            string hex = string.Join(" ", data.Skip(i).Take(end - i).Select(b => $"{b:X2}"));
            hex = hex.PadRight(cols * 3);
            Console.Write($"{Colorize(hex, ConsoleColor.Green)}  ");
            // ASCII
            string ascii = new string(data.Skip(i).Take(end - i).Select(b => (b >= 32 && b < 127) ? (char)b : '.').ToArray());
            Console.WriteLine($"{Colorize(ascii, ConsoleColor.Blue)}");
        }
    }

    private string Colorize(string text, ConsoleColor color)
    {
        return $"\u001b[{color switch { ConsoleColor.Cyan => 96, ConsoleColor.Green => 92, ConsoleColor.Blue => 94, _ => 0 }}m{text}\u001b[0m";
    }

    public void EditByte(long offset, byte value)
    {
        if (offset < 0 || offset >= size) throw new ArgumentOutOfRangeException();
        data[offset] = value;
        accessor.Write(offset, value);
    }

    public List<long> FindBytes(byte[] pattern, long start = 0)
    {
        var positions = new List<long>();
        for (long i = start; i <= size - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) positions.Add(i);
        }
        return positions;
    }

    public int ReplaceBytes(byte[] pattern, byte[] replacement, long start = 0)
    {
        if (pattern.Length != replacement.Length) throw new ArgumentException("Длина шаблона и замены должны совпадать");
        var positions = FindBytes(pattern, start);
        foreach (var p in positions)
        {
            for (int j = 0; j < replacement.Length; j++)
            {
                data[p + j] = replacement[j];
                accessor.Write(p + j, replacement[j]);
            }
        }
        return positions.Count;
    }

    public uint ChecksumCRC32()
    {
        var crc = new CRC32();
        return crc.ComputeHash(data);
    }

    public string ChecksumMD5()
    {
        using var md5 = MD5.Create();
        return BitConverter.ToString(md5.ComputeHash(data)).Replace("-", "").ToLower();
    }

    public string ChecksumSHA1()
    {
        using var sha1 = SHA1.Create();
        return BitConverter.ToString(sha1.ComputeHash(data)).Replace("-", "").ToLower();
    }

    public void Save(string outputFile)
    {
        File.WriteAllBytes(outputFile, data);
    }

    public void Dispose()
    {
        accessor.Dispose();
        mmf.Dispose();
    }
}

// Простая реализация CRC32
public class CRC32
{
    private readonly uint[] table;
    public CRC32()
    {
        table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            table[i] = crc;
        }
    }
    public uint ComputeHash(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Использование: dotnet run -- <file> [команда] [опции]");
            return;
        }
        string filename = args[0];
        string command = "view";
        long offset = 0;
        int cols = 16;
        string bytesHex = null, replaceHex = null, checksumType = null, stringSearch = null, output = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o": offset = long.Parse(args[++i]); break;
                case "--cols": cols = int.Parse(args[++i]); break;
                case "-b": bytesHex = args[++i]; break;
                case "--replace-with": replaceHex = args[++i]; break;
                case "-s": stringSearch = args[++i]; break;
                case "-c": checksumType = args[++i]; break;
                case "--output": output = args[++i]; break;
                default:
                    if (args[i] == "view" || args[i] == "edit" || args[i] == "find" || args[i] == "replace" || args[i] == "checksum")
                        command = args[i];
                    break;
            }
        }

        using var editor = new HexEditor(filename, cols);
        try
        {
            switch (command)
            {
                case "view": editor.View(offset); break;
                case "edit":
                    if (string.IsNullOrEmpty(bytesHex)) { Console.WriteLine("Укажите --bytes"); return; }
                    byte val = Convert.ToByte(bytesHex.Trim(), 16);
                    editor.EditByte(offset, val);
                    Console.WriteLine($"Байт по смещению {offset} изменён на {bytesHex}");
                    break;
                case "find":
                    byte[] pattern;
                    if (!string.IsNullOrEmpty(bytesHex))
                    {
                        pattern = bytesHex.Split(' ').Select(s => Convert.ToByte(s, 16)).ToArray();
                    }
                    else if (!string.IsNullOrEmpty(stringSearch))
                    {
                        pattern = Encoding.UTF8.GetBytes(stringSearch);
                    }
                    else { Console.WriteLine("Укажите --bytes или --string"); return; }
                    var positions = editor.FindBytes(pattern, offset);
                    if (positions.Count > 0)
                    {
                        Console.WriteLine($"Найдено вхождений: {positions.Count}");
                        foreach (var p in positions) Console.WriteLine($"  {p:X8}");
                    }
                    else Console.WriteLine("Не найдено");
                    break;
                case "replace":
                    if (string.IsNullOrEmpty(bytesHex) || string.IsNullOrEmpty(replaceHex))
                    { Console.WriteLine("Укажите --bytes и --replace-with"); return; }
                    var pat = bytesHex.Split(' ').Select(s => Convert.ToByte(s, 16)).ToArray();
                    var rep = replaceHex.Split(' ').Select(s => Convert.ToByte(s, 16)).ToArray();
                    if (pat.Length != rep.Length) { Console.WriteLine("Длина шаблона и замены должны совпадать"); return; }
                    int count = editor.ReplaceBytes(pat, rep, offset);
                    Console.WriteLine($"Заменено: {count}");
                    break;
                case "checksum":
                    if (string.IsNullOrEmpty(checksumType)) { Console.WriteLine("Укажите тип контрольной суммы: crc32, md5, sha1"); return; }
                    switch (checksumType.ToLower())
                    {
                        case "crc32": Console.WriteLine($"CRC32: {editor.ChecksumCRC32():X8}"); break;
                        case "md5": Console.WriteLine($"MD5: {editor.ChecksumMD5()}"); break;
                        case "sha1": Console.WriteLine($"SHA1: {editor.ChecksumSHA1()}"); break;
                        default: Console.WriteLine("Неизвестный тип"); break;
                    }
                    break;
                default: Console.WriteLine("Неизвестная команда"); break;
            }
            if (!string.IsNullOrEmpty(output))
            {
                editor.Save(output);
                Console.WriteLine($"Сохранено в {output}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
        }
    }
}
