using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace DumpityLibrary
{
    class CustomAssemblyResolver : DefaultAssemblyResolver
    {
        public void Register(AssemblyDefinition assembly)
        {
            RegisterAssembly(assembly);
        }
    }
}
