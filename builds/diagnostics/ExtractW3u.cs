using System;
using System.IO;
using System.Linq;
using System.Text;
using War3Net.IO.Mpq;

var maps = new[]
{
    @"C:\Users\hp\Documents\Warcraft III\Hero Team Wars\builds\mcp\hero-team-wars\a867c357-0f07-4e33-8e64-5854866357fd\HeroTeamWars_v8-custom-heroes-shared-altar_a867c357-0f07-4e33-8e64-5854866357fd.w3m",
    @"C:\Users\hp\Documents\Warcraft III\Hero Team Wars\builds\inspection\HeroTeamWars_MCP_FIXED_v7_A8F8FD88.w3m",
    @"C:\Users\hp\Documents\Warcraft III\Hero Team Wars\builds\diagnostics\v8-object-roundtrip.w3m"
};
var outDir = @"C:\Users\hp\Documents\Warcraft III\Hero Team Wars\builds\diagnostics";
foreach (var map in maps)
{
    if (!File.Exists(map)) { Console.WriteLine("MISSING " + map); continue; }
    using var archive = MpqArchive.Open(map, loadListFile: true);
    foreach (var name in new[] { "war3map.w3u", "war3map.j", "(listfile)", "(attributes)" })
    {
        try
        {
            using var stream = archive.OpenFile(name);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            var safe = Path.GetFileNameWithoutExtension(map) + "_" + name.Trim('(', ')') ;
            var outPath = Path.Combine(outDir, safe);
            File.WriteAllBytes(outPath, bytes);
            Console.WriteLine(outPath + " " + bytes.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL " + map + " " + name + " " + ex.Message);
        }
    }
}
