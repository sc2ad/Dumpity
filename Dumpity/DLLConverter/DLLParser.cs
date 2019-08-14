using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DLLConverter
{
    public class DLLParser
    {
        public class DLLData
        {
            public class MethodData
            {
                public class ClassData
                {
                    string Namespace { get; }
                    string Class { get; }
                    public ClassData(string ns, string c)
                    {
                        Namespace = ns;
                        Class = c;
                    }
                    public ClassData(MethodReference d)
                    {
                        Namespace = d.DeclaringType.Namespace;
                        Class = d.DeclaringType.Name;
                    }
                    public override string ToString()
                    {
                        return Namespace + "_" + Class;
                    }
                }
                public interface ILoadData
                {
                    string TypeName { get; }
                    string Value { get; }
                }
                public class FloatData : ILoadData
                {
                    public string TypeName { get => "float"; }
                    public string Value { get; }
                    public FloatData(float val)
                    {
                        Value = val.ToString();
                    }
                }
                public class IntData : ILoadData
                {
                    public string TypeName { get => "int"; }
                    public string Value { get; }
                    public IntData(int val)
                    {
                        Value = val.ToString();
                    }
                }
                public class StringData : ILoadData
                {
                    public string TypeName { get => "Il2CppString*"; }
                    public string Value { get; }
                    public StringData(string val)
                    {
                        Value = val;
                    }
                }
                public class VariableData : ILoadData
                {
                    public string TypeName { get; }
                    public string Value { get; }
                }
                public class StructData : VariableData, ILoadData
                {
                    public new string TypeName { get; }
                    public new string Value { get; }
                    public StructData(string typName, string name)
                    {
                        TypeName = typName;
                        Value = name;
                    }
                }
                public class ReferenceData : VariableData, ILoadData
                {
                    public new string TypeName { get => "Il2CppObject*"; }
                    public new string Value { get; }
                    public ReferenceData(string name)
                    {
                        Value = name;
                    }
                }
                public class ParameterData : ILoadData
                {
                    public string Name { get; private set; }
                    public string TypeName { get; set; }
                    public string Value { get => Name; set => Name = value; }
                    public ParameterData(string typeName, string name)
                    {
                        TypeName = typeName;
                        Name = name;
                    }
                    public override string ToString()
                    {
                        return TypeName + " " + Name;
                    }
                }
                public string Header { get; private set; }
                public List<string> MethodsNeeded { get; private set; } = new List<string>();
                public HashSet<ClassData> ClassesNeeded { get; private set; } = new HashSet<ClassData>();
                public HashSet<TypeReference> StructsNeeded { get; private set; } = new HashSet<TypeReference>();
                public List<ParameterData> Parameters { get; private set; } = new List<ParameterData>();
                public List<string> Lines { get; private set; } = new List<string>();
                private static List<ILoadData> PopParams(Stack<ILoadData> stack, MethodReference d)
                {
                    var paramsForStruct = new List<ILoadData>();
                    for (int i = 0; i < d.Parameters.Count; i++)
                    {
                        paramsForStruct.Insert(0, stack.Pop());
                    }
                    return paramsForStruct;
                }
                public static MethodData Parse(MethodDefinition m)
                {
                    MethodData data = new MethodData();
                    
                    data.Header = "static " + data.GetCppTypeString(m.ReturnType) + " " + m.Name + "(";
                    // If method is non-static, add self to params as Il2CppObject*
                    if (!m.IsStatic)
                    {
                        data.Parameters.Add(new ParameterData("Il2CppObject*", "self"));
                    }
                    foreach (var param in m.Parameters)
                    {
                        if (param.IsOut)
                        {
                            // TODO HANDLE OUT PARAMETERS
                        }
                        data.Parameters.Add(new ParameterData(data.GetCppTypeString(param.ParameterType), param.Name));
                    }
                    // Write out parameters to header
                    foreach (var param in data.Parameters)
                    {
                        data.Header += param.ToString() + ", ";
                    }
                    data.Header = data.Header.Remove(data.Header.Length - 2) + ")";

                    // Begin Method Conversion
                    var stack = new Stack<ILoadData>();
                    foreach (var inst in m.Body.Instructions)
                    {
                        switch (inst.OpCode.Code)
                        {
                            case Mono.Cecil.Cil.Code.Nop:
                                // Do nothing
                                break;
                            case Mono.Cecil.Cil.Code.Ldarg_0:
                                // Load 0th parameter onto stack
                                stack.Push(data.Parameters[0]);
                                break;
                            case Mono.Cecil.Cil.Code.Ldarg_1:
                                stack.Push(data.Parameters[1]);
                                break;
                            case Mono.Cecil.Cil.Code.Ldarg_2:
                                stack.Push(data.Parameters[2]);
                                break;
                            case Mono.Cecil.Cil.Code.Ldarg_3:
                                stack.Push(data.Parameters[3]);
                                break;
                            case Mono.Cecil.Cil.Code.Ldarg_S:
                                stack.Push(data.Parameters[(int)inst.Operand]);
                                break;
                            case Mono.Cecil.Cil.Code.Ldc_I4_0:
                                stack.Push(new IntData(0));
                                break;
                            case Mono.Cecil.Cil.Code.Ldc_R4:
                                stack.Push(new FloatData((float)inst.Operand));
                                break;
                            case Mono.Cecil.Cil.Code.Ldc_I4_S:
                                stack.Push(new IntData((int)inst.Operand));
                                break;
                            case Mono.Cecil.Cil.Code.Ldstr:
                                stack.Push(new StringData((string)inst.Operand));
                                break;
                            case Mono.Cecil.Cil.Code.Newobj:
                                // This is where stuff gets strange. Need to make sure it isn't a struct that we are creating
                                // If it is, we consume the parameters via {}
                                // Otherwise, we consume the parameters via a New call
                                var mdef_ctor = inst.Operand as MethodReference;
                                if (mdef_ctor.DeclaringType.IsValueType && !mdef_ctor.DeclaringType.IsPrimitive)
                                {
                                    // Struct, most likely
                                    data.StructsNeeded.Add(mdef_ctor.DeclaringType);
                                    string st_ctor = data.GetCppTypeString(mdef_ctor.DeclaringType) + " _struct_" + inst.Offset + " = {";
                                    var paramsForStruct = PopParams(stack, mdef_ctor);
                                    for (int i = 0; i < paramsForStruct.Count; i++)
                                    {
                                        if (i > 0) st_ctor += ", ";
                                        st_ctor += paramsForStruct[i].Value;
                                    }
                                    data.Lines.Add(st_ctor + "}");
                                    stack.Push(new StructData(data.GetCppTypeString(mdef_ctor.DeclaringType), "_struct_" + inst.Offset));
                                } else
                                {
                                    // Class, most likely
                                    // Need to have desired class
                                    var cdata = new ClassData(mdef_ctor);
                                    data.ClassesNeeded.Add(cdata);
                                    // Create call to il2cpp_utils::New
                                    string cl_ctor = "Il2CppObject* _class_" + inst.Offset + " = il2cpp_utils::New(" + cdata.ToString();
                                    var paramsForStruct = PopParams(stack, mdef_ctor);
                                    for (int i = 0; i < paramsForStruct.Count; i++)
                                    {
                                        cl_ctor += ", ";
                                        cl_ctor += paramsForStruct[i].Value;
                                    }
                                    data.Lines.Add(cl_ctor);
                                    stack.Push(new ReferenceData("_class_" + inst.Offset));
                                }
                                break;
                            case Mono.Cecil.Cil.Code.Call:
                                // Call varies from CallVirt, we want to find the method we are attempting to call
                                // And add the method and class that we are calling to the desired calls
                                // IF WE ARE CALLING ANOTHER METHOD FROM WITHIN THIS CLASS/ASSEMBLY, WE NEED TO DO SOMETHING DIFFERENT
                                break;
                            case Mono.Cecil.Cil.Code.Stloc_0:
                                data.Lines.Add("auto loc_0 = " + stack.Pop().Value);
                                break;
                            case Mono.Cecil.Cil.Code.Stloc_1:
                                data.Lines.Add("auto loc_1 = " + stack.Pop().Value);
                                break;
                            case Mono.Cecil.Cil.Code.Stloc_2:
                                data.Lines.Add("auto loc_2 = " + stack.Pop().Value);
                                break;
                            case Mono.Cecil.Cil.Code.Stloc_3:
                                data.Lines.Add("auto loc_3 = " + stack.Pop().Value);
                                break;
                            case Mono.Cecil.Cil.Code.Stloc_S:
                                data.Lines.Add("auto loc_" + (int)(inst.Operand) + " = " + stack.Pop().Value);
                                break;
                            case Mono.Cecil.Cil.Code.Ldloc_0:
                                stack.Push(new ReferenceData("loc_0"));
                                break;
                            case Mono.Cecil.Cil.Code.Ldloc_1:
                                stack.Push(new ReferenceData("loc_1"));
                                break;
                            case Mono.Cecil.Cil.Code.Ldloc_2:
                                stack.Push(new ReferenceData("loc_2"));
                                break;
                            case Mono.Cecil.Cil.Code.Ldloc_3:
                                stack.Push(new ReferenceData("loc_3"));
                                break;
                            case Mono.Cecil.Cil.Code.Ldloc_S:
                                stack.Push(new ReferenceData("loc_" + (int)(inst.Operand)));
                                break;
                            default:
                                break;
                        }
                        if (inst.OpCode.Code == Mono.Cecil.Cil.Code.Nop)
                        {
                            // Do nothing with Nop calls
                        }

                        Console.WriteLine($"Instruction: {inst.ToString()}");
                    }
                    // Ret
                    return data;
                }
                public string GetCppTypeString(TypeReference t)
                {
                    if (!t.IsPrimitive)
                    {
                        if (t.IsValueType)
                        {
                            // This could be a struct! If so, we need to add it to the structs required!
                            StructsNeeded.Add(t);
                            return t.Name;
                        }
                        if (t.MetadataType == MetadataType.String)
                            return "Il2CppString*";

                        return "Il2CppObject*";
                    }
                    switch (t.MetadataType)
                    {
                        case MetadataType.Boolean:
                            return "bool";
                        case MetadataType.Byte:
                            return "char";
                        case MetadataType.Char:
                            return "char16_t";
                        case MetadataType.Double:
                            return "double";
                        case MetadataType.Int16:
                            return "short";
                        case MetadataType.Int32:
                            return "int";
                        case MetadataType.Int64:
                            return "long";
                        case MetadataType.SByte:
                            return "uint8_t";
                        case MetadataType.Single:
                            return "float";
                        case MetadataType.UInt16:
                            return "uint16_t";
                        case MetadataType.UInt32:
                            return "uint32_t";
                        case MetadataType.UInt64:
                            return "uint64_t";
                        case MetadataType.Void:
                            return "void";
                        default:
                            return "void*";
                    }
                }
            }
        }
    }
}
