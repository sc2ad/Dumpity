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
                case MetadataType.Boolean:
                    methodName = "ReadAlignedBool";
                    break;
                case MetadataType.Byte:
                    methodName = "ReadAlignedByte";
                    break;
                case MetadataType.Char:
                    methodName = "ReadAlignedChar";
                    break;
                case MetadataType.Double:
                    methodName = "ReadDouble";
                    break;
                case MetadataType.Int16:
                    methodName = "ReadInt16";
                    break;
                case MetadataType.Int64:
                    methodName = "ReadInt64";
                    break;
                case MetadataType.UInt16:
                    methodName = "ReadUInt16";
                    break;
                case MetadataType.UInt32:
                    methodName = "ReadUInt32";
                    break;
                case MetadataType.UInt64:
                    methodName = "ReadUInt64";
                    break;
            }
            return ReaderType.GetMethod(methodName);
        }

        public static void WriteReadPrimitive(ILProcessor worker, MethodDefinition method, TypeDefinition thisType, FieldDefinition f)
        {
            // Write a primitive read line
            var callCode = worker.Create(OpCodes.Callvirt, thisType.Module.ImportReference(GetReadPrimitive(f.FieldType.MetadataType)));
            // Duplicate the reference
            worker.Emit(OpCodes.Dup);
            // Put Reader onto stack
            worker.Emit(OpCodes.Ldarg, method.Parameters[0]);
            // Call reader.ReadSOMEPRIMITIVE
            worker.Append(callCode);
            // Set the field of the object 
            worker.Emit(OpCodes.Stfld, f);
            Console.WriteLine($"Writing {f.Name} as {f.FieldType}");
            f.IsPublic = true;
            f.IsPrivate = false;
        }

        public static void WriteReadAlignedString(ILProcessor worker, MethodDefinition method, FieldDefinition f)
        {
            // Write the read aligned string line
            var callCode = worker.Create(OpCodes.Call, f.Module.ImportReference(ReaderType.GetMethod("ReadAlignedString")));
            // Duplicate the reference
            worker.Emit(OpCodes.Dup);
            // Put Reader onto stack
            worker.Emit(OpCodes.Ldarg, method.Parameters[0]);
            // Call reader.ReadAlignedString
            worker.Append(callCode);
            // Set the field of the object 
            worker.Emit(OpCodes.Stfld, f);
        }

        public static void WriteReadPointer(ILProcessor worker, MethodDefinition method, FieldDefinition f)
        {
            // ASSUMING THE LOCAL FIELD F IS A POINTER!
            // Write the read aligned string line
            var callCode = worker.Create(OpCodes.Newobj, f.Module.ImportReference(typeof(AssetPtr).GetConstructor(new Type[] { ReaderType })));
            // Duplicate the reference
            worker.Emit(OpCodes.Dup);
            // Put Reader onto stack
            worker.Emit(OpCodes.Ldarg, method.Parameters[0]);
            // Call reader.ReadAlignedString
            worker.Append(callCode);
            // Set the field of the object 
            worker.Emit(OpCodes.Stfld, f);
        }

        public static void WriteReadClass(ILProcessor worker, MethodDefinition method, FieldDefinition f, MethodDefinition read)
        {
            // Write the read object line
            var callCode = worker.Create(OpCodes.Call, f.Module.ImportReference(read));
            // Duplicate the reference
            worker.Emit(OpCodes.Dup);
            // Put Reader onto stack
            worker.Emit(OpCodes.Ldarg, method.Parameters[0]);
            // Put length onto stack
            worker.Emit(OpCodes.Ldc_I4_0);
            // Call SomeObject.ReadFrom()
            worker.Append(callCode);
            // Set the field of the object 
            worker.Emit(OpCodes.Stfld, f);
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
            worker.Emit(OpCodes.Dup);
            // Put the reader onto the stack
            worker.Emit(OpCodes.Ldarg, method.Parameters[0]);
            // Call ReadPrefixedArray()
            worker.Append(callCode);
            // Set the field
            worker.Emit(OpCodes.Stfld, f);
            //worker.Emit(OpCodes.Dup);
        }

        public static void WriteReadStruct(ILProcessor worker, MethodDefinition method, FieldDefinition f, TypeDefinition s)
        {
            var structVar = new VariableDefinition(f.Module.ImportReference(s));

            method.Body.Variables.Add(structVar);
            method.Body.InitLocals = true;

            worker.Emit(OpCodes.Ldloca_S, structVar);
            worker.Emit(OpCodes.Initobj, f.Module.ImportReference(s));

            foreach (var field in s.Fields)
            {
                if (field.FieldType.IsPrimitive)
                {
                    var m = f.Module.ImportReference(GetReadPrimitive(field.FieldType.MetadataType));
                    // Ldloca
                    worker.Emit(OpCodes.Ldloca_S, structVar);
                    // Load reader
                    worker.Emit(OpCodes.Ldarg_0);
                    // Call m
                    worker.Emit(OpCodes.Call, m);
                    // Set field
                    worker.Emit(OpCodes.Stfld, f.Module.ImportReference(field));
                }
                else
                {
                    // Field might be an enum!
                    if (field.FieldType.MetadataType == MetadataType.ValueType)
                    {
                        var m = f.Module.ImportReference(ReaderType.GetMethod("ReadInt32"));
                        // Ldloca
                        worker.Emit(OpCodes.Ldloca_S, structVar);
                        // Load reader
                        worker.Emit(OpCodes.Ldarg_0);
                        // Call m
                        worker.Emit(OpCodes.Call, m);
                        // Set field
                        worker.Emit(OpCodes.Stfld, f.Module.ImportReference(field));
                    } else
                    {
                        throw new Exception("Field in struct is NOT primitive");
                    }
                }
            }
            worker.Emit(OpCodes.Dup);
            worker.Emit(OpCodes.Ldloc_S, structVar);
            worker.Emit(OpCodes.Stfld, f);
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
                    if (!f.FieldType.FullName.Contains("UnityEngine"))
                    {
                        break;
                    }
                    Console.WriteLine($"Writing {f.Name} as struct with type: {f.FieldType}");
                    f.IsPublic = true;
                    f.IsPrivate = false;
                    WriteReadStruct(worker, method, f, f.FieldType.Resolve());
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
                        //worker.Emit(OpCodes.Dup));
                        //worker.Emit())
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
            
            var newConstructor = new MethodDefinition(".ctor", MethodAttributes.Public 
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, def.Module.TypeSystem.Void);
            Console.WriteLine($"Is the new method a constructor? {newConstructor.IsConstructor}");

            //newConstructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            //newConstructor.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
            newConstructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

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
            worker.Emit(OpCodes.Newobj, constructor);


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
            worker.Emit(OpCodes.Ret);
            Console.WriteLine($"Added: {method} to type: {thisType}");
            Console.WriteLine("=================================COMPLETED READ METHOD=================================");
            thisType.Methods.Add(method);
            return method;
        }

        public static void Test()
        {
            AssemblyDefinition unityDef = AssemblyDefinition.ReadAssembly("UnityEngine.CoreModule.dll");
            var attr = unityDef.MainModule.GetType("UnityEngine.SerializeField");
            Console.WriteLine($"Serialize Field: {attr}");

            AssemblyDefinition csharpDef = AssemblyDefinition.ReadAssembly("Assembly-CSharp.dll");
            var stringType = csharpDef.MainModule.TypeSystem.String;
            var resolver = new CustomAssemblyResolver();
            var moduleParameters = new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                AssemblyResolver = resolver
            };
            ReaderType = typeof(CustomBinaryReader);
            AssetPtrType = typeof(AssetPtr);
            SerializeFieldAttr = attr;

            //var newName = new AssemblyNameDefinition("Assembly-CSharp-modified", new Version("1.0.0"));
            //AssemblyDefinition output = AssemblyDefinition.CreateAssembly(newName, "Assembly-CSharp-modified.dll", moduleParameters);
            //resolver.Register(output);
            //var moduleDef = output.MainModule;

            //moduleDef.Types.Clear();

            //foreach (var f in Directory.GetFiles(Directory.GetCurrentDirectory(), "*.dll"))
            //{
            //    if (f.EndsWith(".dll"))
            //    {
            //        AssemblyDefinition.ReadAssembly(f);
            //    }
            //}

            //var type = csharpDef.MainModule.GetType("BeatmapLevelSO");
            //var simpleColor = csharpDef.MainModule.GetType("SimpleColorSO");
            //var cMangager = csharpDef.MainModule.GetType("ColorManager");

            foreach (TypeDefinition oldType in csharpDef.MainModule.GetTypes())
            {
                if (!oldType.IsClass || oldType.Name.StartsWith("<"))
                {
                    Console.WriteLine($"Skipping {oldType} because it is not a class!");
                    continue;
                }
                List<FieldDefinition> serialized = FindSerializedData(oldType);
                if (serialized.Count == 0)
                {
                    Console.WriteLine($"Skipping {oldType} because it has no serializable fields!");
                    continue;
                }

                //var newType = new TypeDefinition(oldType.Namespace, oldType.Name, oldType.Attributes);

                //// Populate all fields of the newType from the old type
                //foreach (var f in oldType.Fields)
                //{
                //    newType.Fields.Add(new FieldDefinition(f.Name, f.Attributes, f.FieldType));
                //}

                Console.WriteLine("====================================STARTING TYPE=========================================");
                Console.WriteLine($"Type Name: {oldType}");
                foreach (var f in serialized)
                {
                    Console.WriteLine($"Serializable Field: {f}");
                }
                GenerateReadMethod(serialized, oldType);
                Console.WriteLine($"Adding type: {oldType} to the set of types");
                //if (moduleDef.Types.ToList().Find(t => t.FullName == oldType.FullName) == null)
                //    moduleDef.Types.Add(oldType);
                //var q = Console.ReadKey();
                //if (q.Key == ConsoleKey.Q)
                //{
                //    // End!
                //    csharpDef.Write("Assembly-CSharp-modified-BeatmapLevelSO.dll");
                //    return;
                //}
            }
            //Console.WriteLine($"Writing assembly: {output.MainModule.Name}");
            //var stream = new MemoryStream();
            //output.Write(stream);
            //File.WriteAllBytes(output.MainModule.Name, stream.ToArray());
            csharpDef.Name.Name = "Assembly-CSharp-modified";
            csharpDef.Write(csharpDef.Name.Name + ".dll");
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
