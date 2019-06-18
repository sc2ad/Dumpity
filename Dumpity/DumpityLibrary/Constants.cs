﻿using System;
using System.Collections.Generic;
using System.Text;

namespace DumpityLibrary
{
    class Constants
    {
        public const string MonoBehaviourTypeName = "MonoBehaviour";
        public const string ScriptableObjectTypeName = "ScriptableObject";
        public static readonly List<string> ForbiddenSuffixes = new List<string>()
        {
            MonoBehaviourTypeName,
            ScriptableObjectTypeName,
        };
    }
}
