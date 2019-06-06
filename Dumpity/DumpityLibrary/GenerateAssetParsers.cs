using System;
using System.Collections.Generic;
using System.IO;
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

        public static MethodInfo GetReadPrimitive(string name)
        {
            string methodName = "";
            switch (name)
            {
                case "System.Single":
                    methodName = "ReadSingle";
                    break;
                case "System.Int32":
                    methodName = "ReadInt32";
                    break;
                case "System.String":
                    methodName = "ReadAlignedString";
                    break;
            }
            return typeof(BinaryReader).GetMethod(methodName);
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
                    var callCode = worker.Create(OpCodes.Call, thisType.Module.Import(GetReadPrimitive(f.FieldType.FullName)));
                    // Duplicate the reference
                    worker.Append(worker.Create(OpCodes.Dup));
                    // Put Reader onto stack
                    worker.Append(worker.Create(OpCodes.Ldarg, method.Parameters[0]));
                    // Call reader.ReadSOMEPRIMITIVE
                    worker.Append(callCode);
                    // Set the field of the object 
                    worker.Append(inst);
                } else
                {
                    //TODO Handle writing non-primitives
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
