using Mono.Cecil;
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
        internal enum WritingState
        {
            UnWritten,
            Writing,
            Written
        }
        internal List<FieldDefinition> Fields { get; }
        internal TypeDefinition Type { get; }
        internal string Name { get; }
        internal WritingState State { get; set; }
        internal bool IsStruct { get; }
        internal bool IsEnum { get; }
        internal bool IsClass { get; }
        internal string TypeName
        {
            get
            {
                if (IsStruct || IsEnum)
                    return Name;
                return Name + "*";
            }
        }

        public StructData(TypeDefinition def)
        {
            Name = def.Name.Replace("`", "_").Replace("<", "").Replace(">", "_");
            Type = def;
            State = WritingState.UnWritten;
            IsEnum = Type.IsEnum;
            IsStruct = Type.IsValueType && Type.HasFields && !Type.IsArray && !IsEnum;
            IsClass = Type.IsClass && !IsEnum && !IsStruct;
            Fields = new List<FieldDefinition>();
            foreach (var f in def.Fields)
            {
                if (!IsEnum)
                {
                    if (f.IsStatic || f.IsSpecialName || f.Constant != null)
                    {
                        continue;
                    }
                }
                bool cont = false;
                foreach (string suff in Constants.ForbiddenSuffixes)
                {
                    if (f.Name.EndsWith(suff))
                    {
                        cont = true;
                        break;
                    }
                }
                if (cont)
                    continue;
                Fields.Add(f);
            }
        }

        public override bool Equals(object obj)
        {
            return obj is StructData data &&
                   Name == data.Name;
        }

        public override int GetHashCode()
        {
            return 539060726 + EqualityComparer<string>.Default.GetHashCode(Name);
        }
    }
    public class HookGenerator
    {
        internal string FileName { get; }
        internal string FileHeaderName { get; }
        internal List<MethodData> Methods { get; }
        internal List<StructData> Structs { get; private set; }
        private List<MethodData> Hooks { get; }
        public HookGenerator(string fName)
        {
            FileName = fName;
            FileHeaderName = fName.Replace(".c", ".h");
            Methods = new List<MethodData>();
            Hooks = new List<MethodData>();
        }
        public bool ValidateType(TypeDefinition def)
        {
            if (def == null || def.FullName == null)
            {
                return false;
            }
            if (def.FullName.Contains("System"))
            {
                // Don't add default type methods
                return false;
            }
            if (IsStruct(def))
            {
                return true;
            }
            if (def.IsGenericInstance || def.IsGenericParameter || def.ContainsGenericParameter || !def.IsClass || !def.Fields.Any(f => f.CustomAttributes.Count >= 1
            && f.CustomAttributes[0].Fields.Count == 1 && f.CustomAttributes[0].Fields[0].Name == "Offset"))
            {
                // Don't add generics, or anything that doesn't have a FieldOffset custom attribute
                return false;
            }
            return true;
        }
        public void Add(TypeDefinition def)
        {
            if (!ValidateType(def))
            {
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
        private StructData AddStruct(TypeDefinition d)
        {
            var existing = Structs.Find(sd => sd.Name == d.Name && sd.State == StructData.WritingState.Written);
            if (existing != null)
            {
                Console.WriteLine("Using existing struct: " + existing.Name);
                return existing;
            }
            //if (Structs.Find(sd => sd.Name == d.Name && sd.State == StructData.WritingState.Writing) != null)
            //{
            //    // The struct we are trying to add is being written right now, we have to deal with circular some how
            //    // In this case, we shall return null.
            //    return null;
            //}
            var st = new StructData(d);
            Console.WriteLine("Added Struct: " + st.Name);
            Structs.Add(st);
            return st;
        }
        private void WriteMainHeader(StreamWriter writer)
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
#include """ + FileHeaderName + @"""
");
        }

        private void WriteHeaderHeader(StreamWriter writer)
        {
            // Empty, for now.
        }

        private List<StructData> GetAllStructs()
        {
            if (Structs == null)
            {
                Structs = new List<StructData>();
                foreach (var method in Methods)
                {
                    if (!method.Definition.IsStatic)
                    {
                        // Then we need to make the base type of this method a struct.
                        if (Structs.FirstOrDefault(sd => sd.Name == method.Definition.DeclaringType.Name) == null)
                        {
                            // Add the struct if there are no existing matches
                            AddStruct(method.Definition.DeclaringType);
                        }
                    }
                }
                Console.WriteLine("Found: " + Structs.Count + " structs!");
            }
            return Structs;
        }

        private bool IsStruct(TypeDefinition d)
        {
            return d.IsValueType && d.HasFields && !d.IsArray && !IsEnum(d);
        }

        private bool IsEnum(TypeDefinition d)
        {
            return d.IsEnum;
        }

        private void WriteTypeToBuilder(string name, StreamWriter writer_h, TypeReference t, StringBuilder q, string suffix)
        {
            if (IsPrimitive(t))
            {
                // If the field is primitive, write it.
                q.Append(GetPrimitiveName(t.Name));
            }
            else
            {
                // Otherwise...
                var fd = t.Resolve();
                if (!ValidateType(fd))
                {
                    q.Append("void*");
                }
                //else if (IsEnum(fd))
                //{
                //    // If the field is an enum, write it as an enum.

                //    q.Append("int");
                //}
                else
                {
                    // Also includes enums
                    if (Structs.Find(sd => sd.Name == fd.Name) == null)
                    {
                        // There is no matching struct with this name
                        //Console.WriteLine("Recurse WriteStruct... Adding and writing struct: " + fd.Name);
                        if (ValidateType(fd))
                        {
                            WriteStruct(writer_h, AddStruct(fd));
                        }
                    }
                    var matchName = Structs.Find(sd => sd.Name == fd.Name);
                    string o = "";
                    if (matchName != null && (matchName.IsStruct || matchName.IsClass))
                        o = "struct ";
                    else if (matchName != null && matchName.IsEnum)
                        o = "enum ";
                    q.Append(o + GetTypeName(fd));
                }
            }
            if (name.StartsWith("<"))
            {
                q.Append(" " + name.Replace("<", "").Replace(">", "_") + suffix);
            }
            else
            {
                q.Append(" " + name + suffix);
            }
        }

        private void WriteStruct(StreamWriter writer, StructData s)
        {
            if (Structs.Find(sd => sd.Name == s.Name && sd.State == StructData.WritingState.Written) != null)
            {
                // Already wrote this struct.
                return;
            }
            s.State = StructData.WritingState.Writing;
            StringBuilder b = new StringBuilder();
            // Predefine
            if (s.IsEnum)
            {
                // Never need to predefine for enums, they are always just literal
                b.AppendLine("enum " + s.Name + " {");
                foreach (var f in s.Fields)
                {
                    // Specially named to avoid enum conflicts (because global namespace)
                    b.AppendLine("\t" + s.Name + "_" + f.Name + ",");
                }
                b.Length -= 3;
                b.AppendLine("\n};");
                writer.Write(b.ToString());
            }
            else
            {
                if (s.Fields == null || s.Fields.Count == 0)
                {
                    Structs.Remove(s);
                    return;
                }
                writer.WriteLine("struct " + s.Name + ";");
                b.AppendLine("typedef struct __attribute__((__packed__)) " + s.Name + " {");
                // Assumes that there is always at least one field in the struct
                if (!s.Type.IsValueType)
                {
                    b.AppendLine("\tchar _unused_data_useless[" + GetFieldOffset(s.Fields.First()) + "];\n");
                }
                foreach (var f in s.Fields)
                {
                    StringBuilder q = new StringBuilder("\t");
                    //if (!IsPrimitive(f.FieldType.Resolve()))
                    //{
                    //    Console.WriteLine("Struct: " + f.FieldType.Name);
                    //}
                    WriteTypeToBuilder(f.Name, writer, f.FieldType, q, ";");
                    // Slow
                    //var qt = q.ToString();
                    //if (IsEnum(f.FieldType.Resolve()) || qt.Contains("void*"))
                    //{
                    //    q.Replace("struct ", "");
                    //}
                    b.AppendLine(q.ToString());
                }
                writer.Write(b.ToString());
                writer.WriteLine("} " + s.Name + "_t;");
            }
            
            s.State = StructData.WritingState.Written;
        }

        private void WriteStructs(StreamWriter writer_h, List<StructData> structs)
        {
            for (int i = 0; i < structs.Count; i++)
            {
                WriteStruct(writer_h, structs[i]);
            }
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
                case "Byte":
                    return "char";
                case "String":
                    return "cs_string*";
                default:
                    return name;
            }
        }

        private bool IsPrimitive(TypeReference type)
        {
            return GetPrimitiveName(type.Name) != type.Name;
        }

        private void WriteHooks(StreamWriter writer, StreamWriter writer_h, List<MethodData> methods)
        {
            foreach (var m in methods)
            {
                if (m.Definition.IsConstructor)
                {
                    continue;
                }
                Console.WriteLine("Writing hooks for method: " + m.Name);

                StringBuilder b = new StringBuilder("MAKE_HOOK(");
                b.Append(m.Name);
                b.Append(", ");
                b.Append(m.Offset);
                b.Append(", ");
                //b.Append(GetTypeName(m.Definition.ReturnType));
                WriteTypeToBuilder("", writer_h, m.Definition.ReturnType, b, "");
                b.Length--; // Chop off trailing space
                b.Replace("*", "", b.Length - 2, 2);
                if (!m.Definition.ReturnType.IsPrimitive && !IsStruct(m.Definition.ReturnType.Resolve()) && !IsEnum(m.Definition.ReturnType.Resolve()))
                {
                    if (m.Definition.ReturnType.MetadataType != MetadataType.Void)
                    {
                        b.Append("*");
                    }
                }
                if (!m.Definition.IsStatic)
                {
                    // Non static method needs "self" as first parameter
                    b.Append(", ");
                    //var sd = Structs.FirstOrDefault(a => a.Name == m.Definition.DeclaringType.Name);
                    //if (sd != null)
                    //{
                    //    // Then we know there is a matching struct!
                    //    //Console.WriteLine("Found written struct with name: " + sd.Name);
                    //}
                    b.Append("struct " + GetTypeName(m.Definition.DeclaringType));
                    b.Append(" self");
                }
                foreach (var p in m.Definition.Parameters)
                {
                    b.Append(", ");
                    WriteTypeToBuilder(p.Name, writer_h, p.ParameterType, b, "");
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

        private string GetTypeName(TypeDefinition type)
        {
            if (!IsPrimitive(type))
            {
                var temp = Structs.Find(s => s.Name == type.Name);
                if (temp == null)
                {
                    return "void*";
                }
                return temp.TypeName;
            }
            return GetPrimitiveName(type.Name);
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
                WriteMainHeader(writer);
                using (StreamWriter writer_h = new StreamWriter(FileHeaderName))
                {
                    WriteHeaderHeader(writer_h);
                    WriteStructs(writer_h, GetAllStructs());
                    WriteHooks(writer, writer_h, Methods);
                }
                WriteInstallHooks(writer, Hooks);
            }
        }
    }
}
