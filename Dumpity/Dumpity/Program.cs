using System;
using DumpityLibrary;

namespace DumpityScript
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 1)
            {
                GenerateAssetParsers.Test(args[0]);
                return;
            }
            GenerateAssetParsers.Test();
        }
    }
}
