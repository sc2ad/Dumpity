using System;
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
        public static TypeDefinition SerializeFieldAttr { get; private set; }

        public static List<FieldDefinition> FindSerializedData(TypeDefinition def)
        {
            // So if it is a monobehaviour, we need to see what gameobject it is on. 
            // We also need to get all of the inherited fields/properties in the right order to save em.
            var fields = new List<FieldDefinition>();
            foreach (var f in def.Fields)
            {
                //Console.WriteLine($"Trying Field: {f}");
                foreach (var atr in f.CustomAttributes)
                {
                    //Console.WriteLine($"Custom Attribute: {atr.AttributeType.FullName} compared to {SerializeFieldAttr.FullName}");
                    if (atr.AttributeType.FullName.Equals(SerializeFieldAttr.FullName))
                    {
                        Console.WriteLine($"{f.Name} has type: {f.FieldType} and MetadataType: {f.FieldType.MetadataType}");
                        fields.Add(f);
                    }
                }
            }
            return fields;
        }

        public static bool IsSerializable(TypeDefinition t)
        {
            // It contains attributes, also needs to contain Serailizable
            // Possibly only needs to be within the same assembly to serialize? Not sure yet.
            return t.IsSerializable;
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

        public static void WriteReadPrimitive(ILProcessor worker, MethodDefinition method, TypeDefinition thisType, FieldDefinition f)
        {
            // Write a primitive read line
            var callCode = worker.Create(OpCodes.Callvirt, thisType.Module.ImportReference(GetReadPrimitive(f.FieldType.MetadataType)));
            // Duplicate the reference
            worker.Append(worker.Create(OpCodes.Dup));
            // Put Reader onto stack
            worker.Append(worker.Create(OpCodes.Ldarg, method.Parameters[0]));
            // Call reader.ReadSOMEPRIMITIVE
            worker.Append(callCode);
            // Set the field of the object 
            worker.Append(worker.Create(OpCodes.Stfld, f));
            Console.WriteLine($"Writing {f.Name} as {f.FieldType}");
            f.IsPublic = true;
            f.IsPrivate = false;
        }

        public static void WriteReadAlignedString(ILProcessor worker, MethodDefinition method, FieldDefinition f)
        {
            // Write the read aligned string line
            var callCode = worker.Create(OpCodes.Call, f.Module.ImportReference(typeof(CustomBinaryReader).GetMethod("ReadAlignedString")));
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
            var callCode = worker.Create(OpCodes.Newobj, f.Module.ImportReference(typeof(AssetPtr).GetConstructor(new Type[] { ReaderType })));
            // Duplicate the reference
            worker.Append(worker.Create(OpCodes.Dup));
            // Put Reader onto stack
            worker.Append(worker.Create(OpCodes.Ldarg, method.Parameters[0]));
            // Call reader.ReadAlignedString
            worker.Append(callCode);
            // Set the field of the object 
            worker.Append(worker.Create(OpCodes.Stfld, f));
        }

        public static void WriteReadClass(ILProcessor worker, MethodDefinition method, FieldDefinition f, MethodDefinition read)
        {
            // Write the read object line
            var callCode = worker.Create(OpCodes.Call, f.Module.ImportReference(read));
            // Duplicate the reference
            worker.Append(worker.Create(OpCodes.Dup));
            // Put Reader onto stack
            worker.Append(worker.Create(OpCodes.Ldarg, method.Parameters[0]));
            // Put length onto stack
            worker.Append(worker.Create(OpCodes.Ldc_I4_0));
            // Call SomeObject.ReadFrom()
            worker.Append(callCode);
            // Set the field of the object 
            worker.Append(worker.Create(OpCodes.Stfld, f));
        }

        public class A
        {
            public AssetPtr[] a;
        }

        public static void WriteReadClassArray(ILProcessor worker, MethodDefinition method, FieldDefinition f, TypeDefinition t, MethodDefinition read)
        {
            var m = f.Module.ImportReference(ReaderType.GetMethod("ReadPrefixedArray", Type.EmptyTypes));
            
            // Write the read object line
            var callCode = worker.Create(OpCodes.Call, m.MakeGeneric(t));
            // Duplicate the reference
            worker.Append(worker.Create(OpCodes.Dup));
            // Put the reader onto the stack
            worker.Append(worker.Create(OpCodes.Ldarg, method.Parameters[0]));
            // Call ReadPrefixedArray()
            worker.Append(callCode);
            // Set the field
            worker.Append(worker.Create(OpCodes.Stfld, f));
            //worker.Append(worker.Create(OpCodes.Dup));
        }

        public static void WriteReadOther(ILProcessor worker, MethodDefinition method, FieldDefinition f)
        {
            // If the value is a string, class; we need to read it right away.
            // String = AlignedString (most of the time? Always? Not sure)
            // Class = Pointer, ONLY WHEN THE CLASS DOES NOT HAVE SERIALIZABLE ATTRIBUTE
            var type = f.FieldType.MetadataType;
            switch (type)
            {
                case MetadataType.String:
                    Console.WriteLine($"Writing {f.Name} as aligned string");
                    WriteReadAlignedString(worker, method, f);
                    f.IsPublic = true;
                    f.IsPrivate = false;
                    break;
                case MetadataType.ValueType:
                    // Structs are always serialized
                    Console.WriteLine($"Writing {f.Name} as struct with type: {f.FieldType}");
                    f.IsPublic = true;
                    f.IsPrivate = false;
                    foreach (var field in f.FieldType.Resolve().Fields)
                    {
                        if (field.FieldType.IsPrimitive)
                        {
                            // Read Primitive
                            WriteReadStructPrimitive(worker, method, f.DeclaringType, field);
                        } else
                        {
                            // Read Class
                            //WriteReadOther(worker, method, f.DeclaringType, field);
                            throw new Exception("Struct with class!");
                        }
                    }
                    break;
                case MetadataType.Class:
                    Console.WriteLine($"{f.FieldType.FullName} is the type of field: {f.Name}");
                    if (IsSerializable(f.FieldType.Resolve()))
                    {
                        Console.WriteLine($"Serializable class found: {f.FieldType.FullName}");
                        f.IsPublic = true;
                        f.IsPrivate = false;
                        // Create a read method if it doesn't exist already in that class.
                        var readMethod = GenerateReadMethod(FindSerializedData(f.FieldType.Resolve()), f.FieldType.Resolve());
                        WriteReadClass(worker, method, f, readMethod);
                    } else
                    {
                        // No custom attributes.
                        // This should be a pointer.
                        // Need to add a field and make it public here.
                        Console.WriteLine($"Writing {f.Name} as a pointer with attributes: {f.Attributes}");
                        // Create the public field for the pointer!
                        var assetF = new FieldDefinition(f.Name + "Ptr", Mono.Cecil.FieldAttributes.Public, f.Module.ImportReference(AssetPtrType));
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
                        Console.WriteLine($"Writing {t.Name} as an Object to read/write from!");
                        var readMethod = GenerateReadMethod(FindSerializedData(t), t);
                        Console.WriteLine($"Writing {f.Name} as an Array of {t.Name}");
                        WriteReadClassArray(worker, method, f, t, readMethod);
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
            return new ParameterDefinition("reader", Mono.Cecil.ParameterAttributes.None, def.ImportReference(typeof(CustomBinaryReader)));
        }

        public static ParameterDefinition GetLengthParameter(ModuleDefinition def)
        {
            return new ParameterDefinition("length", Mono.Cecil.ParameterAttributes.None, def.ImportReference(typeof(int)));
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
            var newConstructor = new MethodDefinition(".ctor", MethodAttributes.Private 
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName 
                | MethodAttributes.HideBySig, def.Module.TypeSystem.Void);
            Console.WriteLine($"Is the new method a constructor? {newConstructor.IsConstructor}");

            def.Methods.Add(newConstructor);
            return newConstructor;
        }

        public static MethodDefinition GenerateReadMethod(List<FieldDefinition> fieldsToWrite, TypeDefinition thisType)
        {
            if (thisType.Methods.Any(item => item.Name == "ReadFrom" && item.IsStatic))
            {
                // Method already exists, don't make a new one.
                Console.WriteLine("ReadFrom method already exists!");
                return thisType.Methods.ToList().Find(item => item.Name == "ReadFrom" && item.IsStatic);
            }
            Console.WriteLine("=================================GENERATING READ METHOD=================================");
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
                if (f.FieldType.IsPrimitive)
                {
                    WriteReadPrimitive(worker, method, thisType, f);
                } else
                {
                    WriteReadOther(worker, method, f);
                }
            }
            worker.Append(worker.Create(OpCodes.Ret));
            Console.WriteLine($"Added: {method} to type: {thisType}");
            Console.WriteLine("=================================COMPLETED READ METHOD=================================");
            thisType.Methods.Add(method);
            return method;
        }

        public static void Test()
        {
            AssemblyDefinition csharpDef = AssemblyDefinition.ReadAssembly("Assembly-CSharp.dll");
            var type = csharpDef.MainModule.GetType("BeatmapLevelSO");
            var simpleColor = csharpDef.MainModule.GetType("SimpleColorSO");
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
            SerializeFieldAttr = attr;

            List<FieldDefinition> serialized = FindSerializedData(type);
            foreach (var f in serialized)
            {
                Console.WriteLine($"Serializable Field: {f}");
            }
            GenerateReadMethod(serialized, type);
            GenerateReadMethod(FindSerializedData(simpleColor), simpleColor);
            csharpDef.Write("Assembly-CSharp-modified-BeatmapLevelSO.dll");
        }

        public static void WriteWriteToMethod(TypeDefinition type, Type serializeFieldAttr)
        {
            // Somehow need to write the method that matches the AssetParser method, 
            // make it populate the fields that already exist (in the object that is 'type')
            // and then eventually update the assembly after all of this is done

            // For now, let's just create a new type and make it have all of the certain fields,
            // then also have the serialized fields.
            //var fields = type.FindMembers(MemberTypes.All, BindingFlags.Public | BindingFlags.NonPublic, ;

            var serializedFields = FindSerializedData(type);
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
