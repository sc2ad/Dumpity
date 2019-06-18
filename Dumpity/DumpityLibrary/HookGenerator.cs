﻿using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DumpityLibrary
{
    class MethodData
    {
        internal string Offset { get; }
        internal MethodDefinition Definition { get; }
        internal string Name { set;  get; }
        public MethodData(string offset, MethodDefinition def)
        {
            Offset = offset;
            Definition = def;
            Name = (def.DeclaringType.Name + "_" + def.Name).Replace('.', '_');
        }
    }
    class StructData
    {
        internal List<FieldDefinition> Fields { get; }
        internal TypeReference Type { get; }
        internal string Name { get; }
        public StructData(string name, TypeReference refer, List<FieldDefinition> fields)
        {
            Name = name;
            Type = refer;
            Fields = fields;
        }
        public StructData(TypeDefinition def)
        {
            Name = def.Name;
            Type = def;
            Fields = def.Fields.ToArray().ToList();
        }
    }
    public class HookGenerator
    {
        internal string FileName { get; }
        internal List<MethodData> Methods { get; }
        internal HashSet<StructData> Structs { get; private set; }
        private List<MethodData> Hooks { get; }
        public HookGenerator(string fName)
        {
            FileName = fName;
            Methods = new List<MethodData>();
            Hooks = new List<MethodData>();
        }
        public void Add(TypeDefinition def)
        {
            if (def.FullName.Contains("System"))
            {
                // Don't add default type methods
                return;
            }
            if (def.IsGenericInstance || def.IsGenericParameter || def.ContainsGenericParameter || !def.IsClass || !def.Fields.Any(f => f.CustomAttributes.Count >= 1 
            && f.CustomAttributes[0].Fields.Count == 1 && f.CustomAttributes[0].Fields[0].Name == "Offset"))
            {
                // Don't add generics, or anything that doesn't have a FieldOffset custom attribute
                return;
            }
            foreach (var m in def.Methods)
            {
                if (m.CustomAttributes.Count == 0)
                {
                    // Skipping method that has no attributes
                    continue;
                }
                foreach (var c in m.CustomAttributes)
                {
                    // This method might be good!
                    if (c.Fields == null || c.Fields.Count != 2)
                    {
                        // Not the right custom attribute
                        continue;
                    }
                    var offsetVal = c.Fields.FirstOrDefault(ca => ca.Name == "Offset");
                    if (offsetVal.Argument.Value == null)
                    {
                        // Not actually an offset attribute.
                        continue;
                    }
                    if (m.Name.Contains("<"))
                    {
                        // Just skip special methods for now.
                        continue;
                    }
                    // Support for overloaded methods
                    int dupCount = Methods.Count(md => md.Name.StartsWith(m.DeclaringType.Name + "_" + m.Name));
                    var nmd = new MethodData((string)offsetVal.Argument.Value, m);
                    if (dupCount > 0)
                        nmd.Name += "_" + dupCount;
                    Methods.Add(nmd);
                }
            }
        }
        private void WriteHeader(StreamWriter writer)
        {
            writer.Write(@"#include <android/log.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h> 
#include <fcntl.h>
#include <unistd.h>
#include <dirent.h>
#include <linux/limits.h>
#include <sys/sendfile.h>
#include <sys/stat.h>

#include ""../beatsaber-hook/shared/inline-hook/inlineHook.h""
#include ""../beatsaber-hook/shared/utils/utils.h""
");
        }

        private HashSet<StructData> GetAllStructs()
        {
            if (Structs == null)
            {
                Structs = new HashSet<StructData>();
                foreach (var method in Methods)
                {
                    if (!method.Definition.IsStatic)
                    {
                        // Then we need to make the base type of this method a struct.
                        if (Structs.FirstOrDefault(sd => sd.Name == method.Definition.DeclaringType.Name) == null)
                        {
                            // Add the struct if there are no existing matches
                            Structs.Add(new StructData(method.Definition.DeclaringType));
                        }
                    }
                }
                Console.WriteLine("Found: " + Structs.Count + " structs!");
            }
            return Structs;
        }

        private void WriteStructs(StreamWriter writer, HashSet<StructData> structs)
        {
            foreach (var s in structs)
            {
                writer.WriteLine("typedef struct __attribute__((__packed__)) {");
                // Assumes that there is always at least one field in the struct
                var fields = GetValidFields(s);
                writer.WriteLine("\tchar _unused_data_useless[" + GetFieldOffset(fields.First()) + "];\n");
                foreach (var f in fields)
                {
                    bool bk = false;
                    foreach (string str in Constants.ForbiddenSuffixes)
                    {
                        if (f.Name.EndsWith(str))
                        {
                            // Skip writing it
                            bk = true;
                            break;
                        }
                    }
                    if (bk)
                        continue;
                    StringBuilder b = new StringBuilder("\t");
                    if (f.FieldType.IsPrimitive)
                    {
                        b.Append(GetPrimitiveName(f.FieldType.Name));
                    } else
                    {
                        b.Append("void*");
                    }
                    if (f.Name.StartsWith("<"))
                    {
                        b.Append(" " + f.Name.Replace("<", "").Replace(">", "_") + ";");
                    } else
                    {
                        b.Append(" " + f.Name + ";");
                    }
                    writer.WriteLine(b.ToString());
                }
                writer.WriteLine("} " + s.Name + ";");
            }
        }

        private List<FieldDefinition> GetValidFields(StructData s)
        {
            var fields = new List<FieldDefinition>();
            foreach (var f in s.Fields)
            {
                if (f.IsStatic || f.IsSpecialName || f.Constant != null)
                {
                    continue;
                }
                fields.Add(f);
            }
            return fields;
        }

        private string GetFieldOffset(FieldDefinition f)
        {
            foreach (var ca in f.CustomAttributes)
            {
                if (ca.HasFields && ca.Fields.Count == 1 && ca.Fields[0].Name == "Offset")
                {
                    // This is the proper attribute!
                    return (string)ca.Fields[0].Argument.Value;
                }
            }
            throw new ApplicationException("Could not find FieldOffset for type: " + f.FullName + " it has no CustomAttributes!");
        }

        private string GetPrimitiveName(string name)
        {
            switch (name)
            {
                case "Single":
                    return "float";
                case "Boolean":
                    return "char";
                case "Int32":
                    return "int";
                case "UInt32":
                    return "unsigned int";
                case "Int64":
                    return "long";
                case "Char":
                    return "char";
                case "Double":
                    return "double";
                default:
                    return name;
            }
        }

        private string GetTypeName(TypeReference type)
        {
            if (!type.IsPrimitive)
            {
                var temp = Structs.FirstOrDefault(s => s.Name == type.Name);
                if (temp == null)
                {
                    return "void";
                }
                return temp.Name;
            }
            return GetPrimitiveName(type.Name);
        }

        private void WriteHooks(StreamWriter writer, List<MethodData> methods)
        {
            foreach (var m in methods)
            {
                if (m.Definition.IsConstructor)
                {
                    continue;
                }
                StringBuilder b = new StringBuilder("MAKE_HOOK(");
                b.Append(m.Name);
                b.Append(", ");
                b.Append(m.Offset);
                b.Append(", ");
                b.Append(GetTypeName(m.Definition.ReturnType));
                if (!m.Definition.IsStatic)
                {
                    // Non static method needs "self" as first parameter
                    b.Append(", ");
                    if (Structs.FirstOrDefault(a => a.Name == m.Definition.DeclaringType.Name) != null)
                    {
                        // Then we know there is a matching struct!
                        Console.WriteLine("Found written struct with name: " + m.Definition.DeclaringType.Name);
                    }
                    b.Append(GetTypeName(m.Definition.DeclaringType));
                    b.Append(" self");
                }
                foreach (var p in m.Definition.Parameters)
                {
                    b.Append(", ");
                    if (!p.ParameterType.IsPrimitive)
                    {
                        b.Append(GetTypeName(p.ParameterType) + "*");
                    } else
                    {
                        b.Append(GetTypeName(p.ParameterType));
                    }
                    b.Append(" ");
                    b.Append(p.Name);
                }
                b.Append(") {\n");
                // Line inside of hook
                b.Append("\tlog(\"Called ");
                b.Append(m.Name);
                b.Append(" Hook!\");\n");
                b.Append("\t");
                if (m.Definition.ReturnType.MetadataType != MetadataType.Void)
                {
                    b.Append("return ");
                }
                b.Append(m.Name);
                b.Append("(");
                if (!m.Definition.IsStatic)
                {
                    b.Append("self");
                    if (m.Definition.Parameters.Count > 0)
                    {
                        b.Append(", ");
                    }
                }
                for (int i = 0; i < m.Definition.Parameters.Count; i++)
                {
                    b.Append(m.Definition.Parameters[i].Name);
                    if (i != m.Definition.Parameters.Count - 1)
                    {
                        b.Append(", ");
                    }
                }
                b.Append(");\n}\n\n");
                writer.Write(b.ToString());
                Hooks.Add(m);
            }
        }

        private void WriteInstallHooks(StreamWriter writer, List<MethodData> hooks)
        {
            writer.WriteLine("__attribute__((constructor)) void lib_main() {");
            foreach (var m in hooks)
            {
                writer.WriteLine("\tlog(\"Attempting to install hook: " + m.Name + " at offset: " + m.Offset + "\");");
                writer.WriteLine("\tINSTALL_HOOK(" + m.Name + ");");
            }
            writer.WriteLine("\tlog(\"Complete!\");");
            writer.WriteLine("}");
        }

        public void Write()
        {
            using (StreamWriter writer = new StreamWriter(FileName))
            {
                WriteHeader(writer);
                WriteStructs(writer, GetAllStructs());
                WriteHooks(writer, Methods);
                WriteInstallHooks(writer, Hooks);
            }
        }
    }
}
