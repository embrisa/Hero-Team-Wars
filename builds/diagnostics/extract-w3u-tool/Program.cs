using System;
using System.IO;
using System.Text;
using War3Net.IO.Mpq;

namespace ExtractTool
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0) return;
            var mapPath = args[0];
            using var archive = MpqArchive.Open(mapPath, loadListFile: true);
            foreach (var entry in archive)
            {
                if (entry.FileName != null && entry.FileName.EndsWith(".w3u", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("=================================");
                    Console.WriteLine("MAP: " + mapPath);
                    using var stream = archive.OpenFile(entry.FileName);
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var bytes = ms.ToArray();
                    Console.WriteLine($"Found w3u: size={bytes.Length}");
                    Console.WriteLine($"Hex start: {Convert.ToHexString(bytes.Take(32).ToArray())}");
                    using var reader = new BinaryReader(new MemoryStream(bytes));
                    var formatVersion = reader.ReadInt32();
                    Console.WriteLine($"Format version: {formatVersion}");
                    var origCount = reader.ReadInt32();
                    Console.WriteLine($"Orig count: {origCount}");
                    var customCount = reader.ReadInt32();
                    Console.WriteLine($"Custom count: {customCount}");
                    for (int i = 0; i < customCount; i++)
                    {
                        var origId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                        var customId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                        var modCount = reader.ReadInt32();
                        Console.WriteLine($"  CustObj {i}: orig={origId}, custom={customId}, modCount={modCount}");
                        for (int m = 0; m < modCount; m++)
                        {
                            var modId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                            var varType = reader.ReadInt32();
                            var valStr = "";
                            if (varType == 0) valStr = reader.ReadInt32().ToString();
                            else if (varType == 1 || varType == 2) valStr = reader.ReadSingle().ToString();
                            else if (varType == 3)
                            {
                                var sb = new StringBuilder();
                                while (true)
                                {
                                    var b = reader.ReadByte();
                                    if (b == 0) break;
                                    sb.Append((char)b);
                                }
                                valStr = sb.ToString();
                            }
                            var endInt = reader.ReadInt32();
                            Console.WriteLine($"    Mod {m}: id={modId}, type={varType}, val={valStr}, endInt={endInt:X8}");
                        }
                    }
                    return;
                }
            }
        }
    }
}
