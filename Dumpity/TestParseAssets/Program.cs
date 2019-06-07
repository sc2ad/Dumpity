using DumpityDummyDLL;
using System;
using System.IO;

namespace TestParseAssets
{
    class Program
    {
        static void Main(string[] args)
        {
            int offset = 0x30;
            using (var reader = new CustomBinaryReader(File.OpenRead(args[0])))
            {
                //BeatmapLevelSO d = new BeatmapLevelSO();
                BeatmapLevelSO data = BeatmapLevelSO.ReadFrom(reader, offset);
                foreach (var f in data.GetType().GetFields())
                {
                    Console.WriteLine($"Field: {f.Name} with value: {f.GetValue(data)}");
                }
            }
        }
    }
}
