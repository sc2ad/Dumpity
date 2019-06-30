using System;
using System.Collections.Generic;
using System.Text;

namespace DumpityLibrary
{
    public class GenerateAssetsConfig
    {
        public GenerateAssetsConfig(bool generateAssets)
        {
            GenerateAssets = generateAssets;
        }
        internal bool GenerateAssets { get; set; }
    }
}
