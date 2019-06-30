﻿using System;
using System.Collections.Generic;
using System.Text;

namespace DumpityLibrary
{
    class Constants
    {
        public const string Version = "1.0.0";
        public const string MonoBehaviourTypeName = "MonoBehaviour";
        public const string ScriptableObjectTypeName = "ScriptableObject";
        public const string AssetPtrTypeName = "Ptr";
        public const string EnumValueSuffix = "value__";
        public static readonly List<string> ForbiddenSuffixes = new List<string>()
        {
            MonoBehaviourTypeName,
            ScriptableObjectTypeName,
            AssetPtrTypeName,
            EnumValueSuffix,
        };
    }
}
