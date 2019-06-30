using System;
using System.Collections.Generic;
using System.Text;

namespace DumpityLibrary
{
    public class Config
    {
        public bool Verbose { get; set; }
        public GenerateAssetsConfig AssetsConfig { get; set; }
        public HookDumpConfig HookConfig { get; set; }
    }
}
