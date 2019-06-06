﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DumpityDummyDLL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace DumpityLibrary
{
    public class GenerateAssetParsers
    {
        public static Type ReaderType { get; private set; }
        public static Type WriterType { get; private set; }
        public static Type AssetPtrType { get; private set; }

        public static List<FieldInfo> FindSerializedData(Type type, Type serializeFieldAttr)
        {
            return FindSerializedData(type.GetFields(BindingFlags.NonPublic | BindingFlags.Public), serializeFieldAttr);
        }

        public static List<FieldDefinition> FindSerializedData(TypeDefinition def, TypeDefinition serializeDef)
        {
            // So if it is a monobehaviour, we need to see what gameobject it is on. 
            // We also need to get all of the inherited fields/properties in the right order to save em.
            var fields = new List<FieldDefinition>();
            foreach (var f in def.Fields)
            {
                Console.WriteLine($"Trying Field: {f}");
                foreach (var atr in f.CustomAttributes)
                {
                    Console.WriteLine($"Custom Attribute: {atr.AttributeType.FullName} compared to {serializeDef.FullName}");
                    if (atr.AttributeType.FullName.Equals(serializeDef.FullName))
                    {
                        Console.WriteLine("Serializable field!");
                        fields.Add(f);
                    }
                }
            }
            return fields;
        }

        public static List<FieldInfo> FindSerializedData(FieldInfo[] fields, Type serializeFieldAttr)
        {
            var o = new List<FieldInfo>();
            foreach (FieldInfo i in fields)
            {
                if (i.GetCustomAttribute(serializeFieldAttr) != null)
                {
                    o.Add(i);
                }
            }
            return o;
        }

        public static bool IsSerializable(TypeDefinition t)
        {
            // It contains attributes, also needs to contain Serailizable
            // Possibly only needs to be within the same assembly to serialize? Not sure yet.
            var cs = t.Attributes;
            if (!cs.HasFlag(Mono.Cecil.TypeAttributes.Serializable)) return false;
            Console.WriteLine(cs);
            return true;
        }

        public static MethodInfo GetReadPrimitive(MetadataType type)
        {
            string methodName = "";
            switch (type)
            {
                case MetadataType.Single:
                    methodName = "ReadSingle";
                    break;
                case MetadataType.Int32:
                    methodName = "ReadInt32";
                    break;
            }
            return ReaderType.GetMethod(methodName);
        }

        public static void WriteReadAlignedString(ILProcessor worker, MethodDefinition method, FieldDefinition f)
        {
            // Write the read aligned string line
            var callCode = worker.Create(OpCodes.Call, f.Module.Import(typeof(CustomBinaryReader).GetMethod("ReadAlignedString")));
            // Duplicate the reference
            worker.Append(worker.Create(OpCodes.Dup));
            // Put Reader onto stack
            worker.Append(worker.Create(OpCodes.Ldarg, method.Parameters[0]));
            // Call reader.ReadAlignedString
            worker.Append(callCode);
            // Set the field of the object 
            worker.Append(worker.Create(OpCodes.Stfld, f));
        }

        public static void WriteReadPointer(ILProcessor worker, MethodDefinition method, FieldDefinition f)
        {
            // ASSUMING THE LOCAL FIELD F IS A POINTER!
            // Write the read aligned string line
            var callCode = worker.Create(OpCodes.Newobj, f.Module.Import(typeof(AssetPtr).GetConstructor(new Type[] { ReaderType })));
            // Duplicate the reference
            worker.Append(worker.Create(OpCodes.Dup));
            // Put Reader onto stack
            worker.Append(worker.Create(OpCodes.Ldarg, method.Parameters[0]));
            // Call reader.ReadAlignedString
            worker.Append(callCode);
            // Set the field of the object 
            worker.Append(worker.Create(OpCodes.Stfld, f));
        }

        public static void GetReadOther(MethodDefinition method, ILProcessor worker, FieldDefinition f, MetadataType type)
        {
            // If the value is a string, class; we need to read it right away.
            // String = AlignedString (most of the time? Always? Not sure)
            // Class = Pointer, ONLY WHEN THE CLASS DOES NOT HAVE SERIALIZABLE ATTRIBUTE
            switch (type)
            {
                case MetadataType.String:
                    Console.WriteLine($"Writing {f.Name} as aligned string");
                    WriteReadAlignedString(worker, method, f);
                    f.IsPublic = true;
                    f.IsPrivate = false;
                    break;
                case MetadataType.Class:
                    Console.WriteLine($"{f.FieldType.FullName} is the type of field: {f.Name}");
                    if (IsSerializable(f.FieldType.Resolve()))
                    {
                        Console.WriteLine($"Serializable class found: {f.FieldType.FullName}");
                        f.IsPublic = true;
                        f.IsPrivate = false;

                    } else
                    {
                        // No custom attributes.
                        // This should be a pointer.
                        // Need to add a field and make it public here.
                        Console.WriteLine($"Writing {f.Name} as a pointer with attributes: {f.Attributes}!");
                        // Create the public field for the pointer!
                        var assetF = new FieldDefinition(f.Name + "Ptr", Mono.Cecil.FieldAttributes.Public, f.Module.Import(AssetPtrType));
                        f.DeclaringType.Fields.Add(assetF);
                        WriteReadPointer(worker, method, assetF);
                    }
                    break;
                case MetadataType.Array:
                    var t = f.FieldType.Resolve().GetElementType().Resolve();
                    Console.WriteLine($"{t.FullName} is the type in the array at field: {f.Name}");
                    f.IsPublic = true;
                    f.IsPrivate = false;
                    // Need to now recursively call this function, except on the TypeDefinition for the new classes.

                    // If the type is a serializable class, then we already wrote (or will write) a method for it.

                    if (IsSerializable(t))
                    {
                        // Call the object's ReadFrom method for each item
                        //worker.Append(worker.Create(OpCodes.Dup));
                        //worker.Append(worker.Create())
                    } else
                    {
                        // Otherwise, read a pointer for each item.
                        Console.WriteLine($"Writing {t.Name} as a pointer!");
                    }

                    break;
            }
        }

        public static MethodInfo GetReadPointer()
        {
            return ReaderType.GetMethod("");
        }

        public static ParameterDefinition GetBinaryReaderParameter(ModuleDefinition def)
        {
            // necessarily custom binary reader!
            return new ParameterDefinition("reader", Mono.Cecil.ParameterAttributes.None, def.Import(typeof(CustomBinaryReader)));
        }

        public static ParameterDefinition GetLengthParameter(ModuleDefinition def)
        {
            return new ParameterDefinition("length", Mono.Cecil.ParameterAttributes.None, def.Import(typeof(int)));
        }

        public static MethodDefinition GetConstructor(TypeDefinition def)
        {
            foreach (var m in def.Methods)
            {
                if (m.IsConstructor && !m.HasParameters)
                {
                    return m;
                }
            }
            // Need to add a constructor if no constructor can be found without parameters
            return null;
        }

        public static MethodDefinition GenerateReadMethod(List<FieldDefinition> fieldsToWrite, TypeDefinition thisType)
        {
            // Reference: https://stackoverflow.com/questions/35948733/mono-cecil-method-and-instruction-insertion
            // Create constructor method:
            MethodDefinition method = new MethodDefinition("ReadFrom",  MethodAttributes.Public | MethodAttributes.Static, thisType);
            // Add parameters
            method.Parameters.Add(GetBinaryReaderParameter(thisType.Module));
            // Shouldn't need length, but just in case?
            method.Parameters.Add(GetLengthParameter(thisType.Module));
            ILProcessor worker = method.Body.GetILProcessor();

            var constructor = GetConstructor(thisType);
            // Create local object
            worker.Append(worker.Create(OpCodes.Newobj, constructor));


            foreach (var f in fieldsToWrite)
            {
                var inst = worker.Create(OpCodes.Stfld, f);
                if (f.FieldType.IsPrimitive)
                {
                    // Write a primitive read line
                    var callCode = worker.Create(OpCodes.Callvirt, thisType.Module.Import(GetReadPrimitive(f.FieldType.MetadataType)));
                    // Duplicate the reference
                    worker.Append(worker.Create(OpCodes.Dup));
                    // Put Reader onto stack
                    worker.Append(worker.Create(OpCodes.Ldarg, method.Parameters[0]));
                    // Call reader.ReadSOMEPRIMITIVE
                    worker.Append(callCode);
                    // Set the field of the object 
                    worker.Append(inst);
                    Console.WriteLine($"{f.Name} is a primitive field with type: {f.FieldType}");
                    f.IsPublic = true;
                    f.IsPrivate = false;
                } else
                {
                    GetReadOther(method, worker, f, f.FieldType.MetadataType);
                }
            }
            worker.Append(worker.Create(OpCodes.Ret));
            Console.WriteLine($"Added: {method} to type: {thisType}");
            thisType.Resolve();
            return method;
        }

        public static void Test()
        {
            AssemblyDefinition csharpDef = AssemblyDefinition.ReadAssembly("Assembly-CSharp.dll");
            var type = csharpDef.MainModule.GetType("BeatmapLevelSO");
            Console.WriteLine(type);
            AssemblyDefinition unityDef = AssemblyDefinition.ReadAssembly("UnityEngine.CoreModule.dll");
            var attr = unityDef.MainModule.GetType("UnityEngine.SerializeField");
            Console.WriteLine($"Serialize Field: {attr}");
            foreach (var m in csharpDef.Modules)
            {
                Console.WriteLine($"**** START MODULE {m} ****");
                foreach (var t in m.GetTypes())
                {
                    //Console.WriteLine($"Found TypeDefinition: {t}");
                }
            }

            ReaderType = typeof(CustomBinaryReader);
            AssetPtrType = typeof(AssetPtr);

            List<FieldDefinition> serialized = FindSerializedData(type, attr);
            foreach (var f in serialized)
            {
                Console.WriteLine($"Serializable Field: {f}");
            }

            type.Methods.Add(GenerateReadMethod(serialized, type));
            csharpDef.Write("Assembly-CSharp-modified-BeatmapLevelSO.dll");
        }

        public static void WriteWriteToMethod(Type type, Type serializeFieldAttr)
        {
            // Somehow need to write the method that matches the AssetParser method, 
            // make it populate the fields that already exist (in the object that is 'type')
            // and then eventually update the assembly after all of this is done

            // For now, let's just create a new type and make it have all of the certain fields,
            // then also have the serialized fields.
            //var fields = type.FindMembers(MemberTypes.All, BindingFlags.Public | BindingFlags.NonPublic, ;
            var serializedFields = FindSerializedData(type, serializeFieldAttr);
            // Test
            // Need to do UnityEngine first!
            AssemblyDefinition unityDef = AssemblyDefinition.ReadAssembly("UnityEngine.dll");
            TypeDefinition attr;
            foreach (var t in unityDef.MainModule.GetTypes())
            {
                if (t.Name == "SerializeField")
                {
                    // Found Serialize Field at t
                    Console.WriteLine($"Found SerializeField Attribute Class at: {t}");
                    attr = t;
                    break;
                }
            }

            //AssemblyDefinition def = AssemblyDefinition.ReadAssembly("Assembly-CSharp.dll");
            //foreach (var moduleDef in def.Modules)
            //{
            //    var tps = moduleDef.GetTypes();
            //    foreach (var typeDef in tps)
            //    {
            //        // Add the reference for the AssetParser interface
            //        moduleDef.AssemblyReferences.Add();
            //        // implement the interface
            //        typeDef.Interfaces
            //        // Add the new method we just created.
            //        typeDef.Module.Add()
            //    }
            //}
        }
    }
    interface AssetParser
    {
        void WriteTo(BinaryWriter writer);
        void ReadFrom(BinaryReader reader);
    }
}
