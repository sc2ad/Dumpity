using System;
using System.Collections.Generic;
using System.Text;

namespace DumpityDummyDLL
{
    public class AssetPtr
    {
        public int FileID { get; set; }
        public ulong PathID { get; set; }

        public AssetPtr(CustomBinaryReader reader)
        {
            FileID = reader.ReadInt32();
            PathID = reader.ReadUInt64();
        }

        //public void WriteTo()
    }
}
