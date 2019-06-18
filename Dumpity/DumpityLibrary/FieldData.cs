using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace DumpityLibrary
{
    public class FieldData
    {
        public string Name { get; }
        public TypeReference TypeDef { get; }
        public FieldData(string n, TypeReference td, string suffix = "")
        {
            Name = n + suffix;
            TypeDef = td;
        }
        public FieldDefinition GetDefinition(TypeDefinition type)
        {
            var fd = new FieldDefinition(Name, FieldAttributes.Public, TypeDef);
            type.Fields.Add(fd);
            return fd;
        }
    }
}
