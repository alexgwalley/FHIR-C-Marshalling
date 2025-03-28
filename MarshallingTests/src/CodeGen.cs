﻿/*
MIT License

Copyright (c) 2024 Alex Walley

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace FHIR_Marshalling
{
    public class CodeGen
    {
        public enum MemberTypeEnum
        {
            Primative,
            Enum,
            Class,
            Union,
            Array_Of_Primative,
            Array_Of_Enum,
            Array_Of_Classes,
        };

        public struct UnionEntry
        {
            public string MemberEnumName;
            public string NativeUnionFieldName;
            public Type NativeType;
            public FieldInfo NativeField;
            public Type FirelyType;
            public string FirelyFieldName;
        }

        public class MemberMappingInfo
        {
            public MemberTypeEnum MemberType;

            public Type NativeType;
            public FieldInfo NativeField;
            public FieldInfo NativeCountField;
            public string NativeFieldName;

            public Type FirelyType;
            public string FirelyFieldName;

            public string FhirName;

            // NOTE(agw): for enum stuff
            public EnumEntry[] EnumEntries; 

            // NOTE(agw): for union stuff
            public Type EnumType;
            public string NativeEnumFieldName;
            public FieldInfo NativeEnumField;
            public List<UnionEntry> UnionEntries;
        }

        public class MappingInfo
        {
            public string ClassName;
            public Type FirelyType;
            public IList<MemberMappingInfo> Members;
        };

        public static bool IsNativePrimative(Type? type)
        {
            if (type == null) return false;

            return type == typeof(NullableString8) || 
                type == typeof(NullableString8) || 
                type == typeof(ISO8601_Time) || 
                type == typeof(NullableInt32) ||
                type == typeof(NullableDouble) ||
                type == typeof(NullableBoolean) ||
                type.IsPrimitive;
        }

        public struct ConversionEntry
        {
            public Type FirelyType;
            public Type ExpectedNativeType;
            public string SingleFormatString;
            public string DoubleFormatString;
        };

        public static ConversionEntry[] conversionEntries = new ConversionEntry[]
        {
            new ConversionEntry{ FirelyType = typeof(Instant), ExpectedNativeType = typeof(ISO8601_Time), SingleFormatString = "{0}.ToFhirInstant()" },
            new ConversionEntry{ FirelyType = typeof(Date), ExpectedNativeType = typeof(ISO8601_Time), SingleFormatString = "{0}.ToFhirDate()" },
            new ConversionEntry{ FirelyType = typeof(FhirDateTime), ExpectedNativeType = typeof(ISO8601_Time), SingleFormatString = "{0}.ToFhirDateTime()" },
            new ConversionEntry{ FirelyType = typeof(Time), ExpectedNativeType = typeof(ISO8601_Time), SingleFormatString = "{0}.ToFhirTime()" },
            new ConversionEntry{ FirelyType = typeof(FhirBoolean), ExpectedNativeType = typeof(NullableBoolean), SingleFormatString = "{0}.ToFhirBoolean()" },
            new ConversionEntry{ FirelyType = typeof(decimal), ExpectedNativeType = typeof(NullableString8), SingleFormatString = "{0}.DecimalValue()" },
            new ConversionEntry{ FirelyType = typeof(string), ExpectedNativeType = typeof(NullableString8), SingleFormatString = "{0}.ToString()" },
            new ConversionEntry{ FirelyType = typeof(FhirDecimal), ExpectedNativeType = typeof(NullableString8), SingleFormatString = "{0}.ToFhirDecimal()" },
            new ConversionEntry{ FirelyType = typeof(Integer), ExpectedNativeType = typeof(NullableInt32), SingleFormatString = "{0}.ToFhirInteger()" },
            new ConversionEntry{ FirelyType = typeof(PositiveInt), ExpectedNativeType = typeof(NullableInt32), SingleFormatString = "{0}.ToFhirPositiveInt()" },
            new ConversionEntry{ FirelyType = typeof(UnsignedInt), ExpectedNativeType = typeof(NullableInt32), SingleFormatString = "{0}.ToFhirUnsignedInt()" },
            new ConversionEntry{ FirelyType = typeof(Integer64), ExpectedNativeType = typeof(long), SingleFormatString = $"new Integer64({{0}}.{nameof(NullableInt32.GetValue)}())" },
        };


        // TODO(agw): combine this into one
        public static Dictionary<Type, Type> CSTypeFromNativeType = new Dictionary<Type, Type>()
        {
            {typeof(NullableString8), typeof(string) },
            {typeof(int), typeof(int) },
            {typeof(NullableInt32), typeof(int?) },
        };

        public static string CSPrimativeFromNativePrimative(Type nativeType, string accessor)
        {
            if(nativeType == typeof(NullableString8))
            {
                return $"{accessor}.ToString()";
            }
            else if(nativeType == typeof(NullableInt32))
            {
                return $"{accessor}.GetValue()";
            }

            throw new NotImplementedException();
        }

        public static string FirelyPrimativeFromNativePrimative(Type nativeType, Type inFirelyType, string nativeAccessor)
        {
            Type firelyType = inFirelyType;
            if (firelyType.IsGenericType)
            {
                if (Nullable.GetUnderlyingType(firelyType) == null)
                    throw new NotImplementedException();

                firelyType = Nullable.GetUnderlyingType(firelyType);
            }

            foreach (var entry in conversionEntries)
            {
                if (entry.FirelyType != firelyType) continue;

                if (entry.SingleFormatString != null && entry.SingleFormatString.Length > 0)
                {
                    return String.Format(entry.SingleFormatString, nativeAccessor);
                }

                if (entry.DoubleFormatString != null && entry.DoubleFormatString.Length > 0)
                {
                    return String.Format(entry.DoubleFormatString, firelyType.FullName, nativeAccessor);
                }
            }


            if (nativeType == firelyType)
            {
                return $"{nativeAccessor}";
            }
            else if (CSTypeFromNativeType.TryGetValue(nativeType, out Type csType))
            {
                if (csType == firelyType)
                {
                    return nativeAccessor;
                }

                var constructor = firelyType.GetConstructor(new Type[] { csType });
                if (constructor != null)
                {
                    return $"new {firelyType.FullName}({CSPrimativeFromNativePrimative(nativeType, nativeAccessor)})";
                }
            }

            throw new NotImplementedException();
        }

        public static Boolean HasDirectConversion(Type firelyType)
        {
             bool hasDirectConversion = false;
            foreach (var entry in conversionEntries)
            {
                if (entry.FirelyType != firelyType) continue;
                hasDirectConversion = true;
                break;
            }
            return hasDirectConversion;
        }

        // NOTE(agw): returns primative variable name for assignment
        public static string AppendValueCreation(IndentedStringBuilder builder, Type nativeType, string nativeFieldName, string nativeAccessor, Type firelyType)
        {
            string primativeName = $"{nativeFieldName}_primative";

            if (firelyType == typeof(Base64Binary))
            {
                builder.AppendLine($"Base64Binary? {primativeName} = null;");
                builder.AppendLine($"if({nativeAccessor}.str != null) {{");
                builder.IndentedAmount += 1;
                string byteArrayName = $"{nativeFieldName}_arr";
                string length = $"(int){nativeAccessor}.size";

                builder.AppendLine($"byte[] {byteArrayName} = new byte[{length}];");
                builder.AppendLine($"Marshal.Copy((IntPtr){nativeAccessor}.str, {byteArrayName}, 0, {length});");
                builder.AppendLine($"{primativeName} = new Base64Binary({byteArrayName});");

                builder.IndentedAmount -= 1;
                builder.AppendLine($"}}");
            }
            else if(IsNativePrimative(nativeType))
            {
                if(HasDirectConversion(firelyType))
                {
                    string firelyFromCS = FirelyPrimativeFromNativePrimative(nativeType, firelyType, nativeAccessor);
                    builder.AppendLine($"var {primativeName} = {firelyFromCS};");
                }
                else
                {
                    if (nativeType == firelyType)
                    {
                        builder.AppendLine($"var {primativeName} = {nativeAccessor};");
                    }
                    else if (CSTypeFromNativeType.TryGetValue(nativeType, out Type csType))
                    {
                        if (csType == firelyType)
                        {
                            builder.AppendLine($"var {primativeName} = {nativeAccessor};");
                        }

                        var constructor = firelyType.GetConstructor(new Type[] { csType });
                        if (constructor != null)
                        {
                            var csPrimative = CSPrimativeFromNativePrimative(nativeType, nativeAccessor);
                            string temp = $"{primativeName}_temp";
                            builder.AppendLine($"var {temp} = {csPrimative};");
                            builder.AppendLine($"var {primativeName} =  ({temp} == null) ? null : new {firelyType.FullName}({temp});");
                            //return $"new {firelyType.FullName}({})";
                        }
                    }

                }
            }
            else
            {
                if(!NativeToFirely.ContainsKey(nativeType)) { throw new NotImplementedException(); }

                string className = nativeType.Name;
                string deserializeCall = $"Marshal_{className}({nativeAccessor})";
                builder.AppendLine($"var {primativeName} = {deserializeCall};");
                builder.AppendLine("");
            }

            return primativeName;
        }

        public static string AccessorForNative(string paramName, Type classType, FieldInfo nativeField)
        {
            ulong offset = (ulong)Marshal.OffsetOf(classType, nativeField.Name);
            string fullName = nativeField.FieldType.FullName.Replace("+", ".");
            string result = $"(*({fullName}*)((byte*){paramName} + {offset}))";

            return result;
        }

        public static void NullCheckAssignment(IndentedStringBuilder builder, string nativeFieldAccessor,
                                            Type nativeType, string nativeFieldName,
                                            Type firelyType, string firelyFieldName,
                                            string firelyInstance)

        {
            if (firelyType == typeof(Base64Binary))
            {
                var primativeName = AppendValueCreation(builder, nativeType, nativeFieldName, nativeFieldAccessor, firelyType);
                builder.AppendLine($"{firelyInstance}.{firelyFieldName} = {primativeName};");
                builder.AppendLine("");
            }
            else if (IsNativePrimative(nativeType))
            {
                if (HasDirectConversion(firelyType))
                {
                    string firelyFromCS = FirelyPrimativeFromNativePrimative(nativeType, firelyType, nativeFieldAccessor);
                    string tempName = $"{nativeFieldName}_temp";
                    builder.AppendLine($"var {tempName} = {firelyFromCS};");
                    builder.AppendLine($"if({tempName} != null) {{");
                    builder.IndentedAmount += 1;

                    builder.AppendLine($"{firelyInstance}.{firelyFieldName} = {tempName};");

                    builder.IndentedAmount -= 1;
                    builder.AppendLine($"}}");
                    builder.AppendLine("");
                }
                else
                {
                    if (nativeType == firelyType)
                    {
                        builder.AppendLine($"{firelyInstance}.{firelyFieldName} = {nativeFieldAccessor};");
                        builder.AppendLine("");
                    }
                    else if (CSTypeFromNativeType.TryGetValue(nativeType, out Type csType))
                    {
                        if (csType == firelyType)
                        {
                            builder.AppendLine($"{firelyInstance}.{firelyFieldName} = {nativeFieldAccessor};");
                            builder.AppendLine("");
                        }

                        var constructor = firelyType.GetConstructor(new Type[] { csType });
                        if (constructor != null)
                        {
                            var csPrimative = CSPrimativeFromNativePrimative(nativeType, nativeFieldAccessor);
                            string tempName = $"{nativeFieldName}_temp";
                            builder.AppendLine($"var {tempName} = {csPrimative};");
                            builder.AppendLine($"if({tempName} != null) {{");
                            builder.IndentedAmount += 1;

                            builder.AppendLine($"{firelyInstance}.{firelyFieldName} = new {firelyType.FullName}({tempName});");

                            builder.IndentedAmount -= 1;
                            builder.AppendLine($"}}");
                            builder.AppendLine("");
                        }
                    }
                }
            }
            else
            {
                if (!NativeToFirely.ContainsKey(nativeType)) { throw new NotImplementedException(); }

                string className = nativeType.Name;

                builder.AppendLine($"if({nativeFieldAccessor} != null) {{");
                builder.IndentedAmount += 1;

                string deserializeCall = $"Marshal_{className}({nativeFieldAccessor})";
                builder.AppendLine($"{firelyInstance}.{firelyFieldName} = {deserializeCall};");

                builder.IndentedAmount -= 1;
                builder.AppendLine($"}}");
                builder.AppendLine("");
            }

        }

        public static string GetFhirLiquidator(Type nativeType, MappingInfo info)
        {
            var builder = new IndentedStringBuilder();

            var firelyInstance = "fhirInstance";
            string nativeParameter = "in_native";
            // function name
            string firelyTypeFullName = info.FirelyType.FullName.Replace("+", ".");

            string functionName = $"public static {firelyTypeFullName}? Marshal_{nativeType.Name}({nativeType.FullName}* {nativeParameter}) {{";
            builder.AppendLine(functionName);

            builder.IndentedAmount += 1;

            builder.AppendLine($"if({nativeParameter} == null) return null;");

            string firelyInstantiate = $"var {firelyInstance} = ({firelyTypeFullName}) FormatterServices.GetUninitializedObject(typeof({firelyTypeFullName}));";
            builder.AppendLine(firelyInstantiate);

            int index = -1;
            foreach (var member in info.Members)
            {
                index++;
                if (member.NativeType == null) continue;
                switch(member.MemberType)
                {
                    case MemberTypeEnum.Class:
                    case MemberTypeEnum.Primative:
                        {
                            string nativeFieldAccessor = AccessorForNative("in_native", nativeType, member.NativeField);
                            NullCheckAssignment(builder, nativeFieldAccessor,
                                member.NativeType, member.NativeFieldName,
                                member.FirelyType, member.FirelyFieldName,
                                firelyInstance);
                        }
                        break;

                    case MemberTypeEnum.Enum:
                        {
                            var underlying = member.FirelyType.GetGenericArguments()[0];
                            string underlyingFullName = underlying.FullName.Replace("+", ".");

                            string nativeFieldAccessor = AccessorForNative("in_native", nativeType, member.NativeField);
                            string tempCodeName = $"__temp_code{member.FirelyFieldName}";
                            builder.AppendLine($"var {tempCodeName} = {nativeFieldAccessor}.ToString();");
                            builder.AppendLine($"if({tempCodeName} != null) {{");
                            builder.IndentedAmount += 1;
                            var newCode = $"new Code<{underlyingFullName}> ()";
                            builder.AppendLine($"{firelyInstance}.{member.FirelyFieldName} = {newCode};");
                            builder.AppendLine($"{firelyInstance}.{member.FirelyFieldName}.ObjectValue = {tempCodeName};");
                            builder.IndentedAmount -= 1;
                            builder.AppendLine($"}}");
                        }
                        break;


                    case MemberTypeEnum.Union:
                    {
                            string baseAccessor = AccessorForNative("in_native", nativeType, member.NativeEnumField);
                            builder.AppendLine($"switch({baseAccessor}) {{");
                            builder.IndentedAmount += 1;
                            foreach (var entry in member.UnionEntries)
                            {
                                builder.AppendLine($"case {entry.MemberEnumName}: {{");
                                builder.IndentedAmount += 1;

                                string unionStructName = $"{entry.NativeUnionFieldName}__union";

                                string unionAccessor = AccessorForNative("in_native", nativeType, member.NativeField);
                                builder.AppendLine($"var {unionStructName} = &{unionAccessor};");

                                string unionFieldAccessor = AccessorForNative(unionStructName, member.NativeType, entry.NativeField);

                                NullCheckAssignment(builder, unionFieldAccessor,
                                    entry.NativeType, member.NativeFieldName,
                                    entry.FirelyType, member.FirelyFieldName,
                                    firelyInstance);

                                builder.IndentedAmount -= 1;
                                builder.AppendLine($"}} break;");
                            }

                            builder.IndentedAmount -= 1;
                            builder.AppendLine("}");
                    } break;

                    case MemberTypeEnum.Array_Of_Enum:
                    {
                        var underlying = member.FirelyType.GetGenericArguments()[0];
                        string underlyingFullName = underlying.FullName.Replace("+", ".");
                        var listInnerType = $"Code<{underlyingFullName}>";

                        string countAccessor = AccessorForNative("in_native", nativeType, member.NativeCountField);
                        builder.AppendLine($"if((ulong){countAccessor} > 0) {{");
                        builder.IndentedAmount += 1;

                        string listName = $"{member.NativeFieldName}_list";
                        string listInit = $"var {listName}  = new List<{listInnerType}>((int){countAccessor});";
                        builder.AppendLine(listInit);

                        string baseAccessor = AccessorForNative("in_native", nativeType, member.NativeField);
                        string nativeFieldAccessor = $"{baseAccessor}[i]";

                        string forStart = $"for(ulong i = 0; i < (ulong){countAccessor}; i++)";
                        builder.AppendLine(forStart);
                        builder.AppendLine("{");

                        builder.IndentedAmount += 1;
                            var newCode = $"new Code<{underlyingFullName}> ()";
                            builder.AppendLine($"var __code = {newCode};");
                            builder.AppendLine($"__code.ObjectValue = {nativeFieldAccessor}.ToString();");
                            builder.AppendLine($"{listName}.Add(__code);");
                        builder.AppendLine("}");
                        builder.AppendLine("");
                        builder.IndentedAmount -= 1;

                        string assignmentString = $"{firelyInstance}.{member.FirelyFieldName} = {listName};";
                        builder.AppendLine(assignmentString);
                        builder.AppendLine("");

                        builder.AppendLine("}");
                        builder.AppendLine("");
                        builder.IndentedAmount -= 1;


                    } break;

                    case MemberTypeEnum.Array_Of_Primative:
                    {
                            string nativeCountFieldName = member.NativeFieldName + "_count";

                            string listInnerType = "";
                            if (member.FirelyType.IsGenericType)
                            {
                                var underlying = member.FirelyType.GetGenericArguments()[0];
                                string fullName = underlying.FullName.Replace("+", ".");
                                listInnerType = $"Code<{fullName}>";
                            }
                            else
                            {
                                listInnerType = member.FirelyType.Name;
                            }

                            string countAccessor = AccessorForNative("in_native", nativeType, member.NativeCountField);
                            builder.AppendLine($"if((ulong){countAccessor} > 0) {{");
                            builder.IndentedAmount += 1;

                            string listName = $"{member.NativeFieldName}_list";
                            string listInit = $"var {listName}  = new List<{listInnerType}>((int){countAccessor});";
                            builder.AppendLine(listInit);

                            string baseAccessor = AccessorForNative("in_native", nativeType, member.NativeField);
                            string nativeFieldAccessor = $"{baseAccessor}[i]";

                            string forStart = $"for(ulong i = 0; i < (ulong){countAccessor}; i++)";
                            builder.AppendLine(forStart);
                            builder.AppendLine("{");

                            builder.IndentedAmount += 1;

                            var primativeName = AppendValueCreation(builder, member.NativeType, member.NativeFieldName, nativeFieldAccessor, member.FirelyType);
                            builder.AppendLine($"{listName}.Add({primativeName});");

                            builder.AppendLine("}"); builder.AppendLine("");
                            builder.IndentedAmount -= 1;

                            string assignmentString = $"{firelyInstance}.{member.FirelyFieldName} = {listName};";
                            builder.AppendLine(assignmentString);
                            builder.AppendLine("");

                            builder.AppendLine("}"); builder.AppendLine("");
                            builder.IndentedAmount -= 1;

                    }break;

                    case MemberTypeEnum.Array_Of_Classes:
                    {
                            string nativeCountFieldName = member.NativeFieldName + "_count";

                            string listInnerType = member.FirelyType.FullName.Replace("+", ".");

                            string countAccessor = AccessorForNative("in_native", nativeType, member.NativeCountField);
                            builder.AppendLine($"if((ulong){countAccessor} > 0) {{");
                            builder.IndentedAmount += 1;

                            string listName = $"{member.NativeFieldName}_list";
                            string listInit = $"var {listName}  = new List<{listInnerType}>((int){countAccessor});";
                            builder.AppendLine(listInit);

                            string forStart = $"for(ulong i = 0; i < (ulong){countAccessor}; i++)";
                            builder.AppendLine(forStart);
                            builder.AppendLine("{");
                            builder.IndentedAmount += 1;

                            string baseAccessor = AccessorForNative("in_native", nativeType, member.NativeField);
                            string nativeFieldAccessor = $"{baseAccessor}[i]";

                            string localFirelyName = $"{member.NativeFieldName}_firely";
                            string deserializeCall = $"Marshal_{member.NativeType.Name}({nativeFieldAccessor})";
                            string setLocalFirely = $"var {localFirelyName} = {deserializeCall};";
                            builder.AppendLine(setLocalFirely);

                            string listAdditionString = $"{listName}.Add({localFirelyName});";
                            builder.AppendLine(listAdditionString);

                            builder.IndentedAmount -= 1;
                            builder.AppendLine("}"); builder.AppendLine("");

                            string assignmentString = $"{firelyInstance}.{member.FirelyFieldName} = {listName};";
                            builder.AppendLine(assignmentString);
                            builder.AppendLine("");

                            builder.IndentedAmount -= 1;
                            builder.AppendLine("}"); builder.AppendLine("");
                        }
                        break;
                }
            }

            // add field extensions
            {

                string starting = @"if ((ulong)(*(System.UIntPtr*)((byte*)in_native + 8)) > 0)
    {
        FieldExtensionNode* node = (FieldExtensionNode*)((byte*)in_native + 8);

        while(node != null)
        {

            FHIR_Marshalling.Extension** extensions = (FHIR_Marshalling.Extension**)node->extensions;
            if (node->extension_count > 0 && fhirInstance.Extension == null)
            {

                fhirInstance.Extension = new List<Hl7.Fhir.Model.Extension>();
            }

            for (int i = 0; i < node->extension_count; i += 1)
            {
                FHIR_Marshalling.Extension* ext = extensions[i];
                Hl7.Fhir.Model.Extension marshalled = Marshal_Extension(ext);
                string node_name = node->name.ToString();
                switch (node_name)
                {";

                builder.AppendLine(starting);
                // generate based on FhirAttributeName
                builder.IndentedAmount += 4;

                var nativeFields = nativeType.GetFields();
                foreach (var field in nativeFields)
                {
                    FhirNameAttribute fhirAttribute = (FhirNameAttribute) field.GetCustomAttribute(typeof(FhirNameAttribute));
                    string fhirName = "";
                    if (fhirAttribute != null)
                    {
                        fhirName = fhirAttribute.FhirName;
                    }

                    if (fhirName != "" && fhirName != "resourceType" && fhirName != "__field_extensions")
                    {
                        string elementName = fhirName.Capitalize() + "Element";
                        string switchCase = @"case ""{0}"": fhirInstance.{1}.Extension.Add(marshalled); break;".FormatWith(fhirName, elementName);
                        builder.AppendLine(switchCase);
                    }
                }


                builder.IndentedAmount -= 4;

                string ending = @"
                }
            }

            node = node->next;
        }
    }";

                builder.AppendLine(ending);
            }

            builder.AppendLine($"return {firelyInstance};");
            builder.IndentedAmount -= 1;
            builder.AppendLine("}");

            return builder.ToString();
        }

        public static string GetDeserializeResource(Dictionary<Type, Type> typeMap) 
        {
            var builder = new IndentedStringBuilder();
            string firelyResourceName = typeof(Hl7.Fhir.Model.Resource).FullName;
            builder.AppendLine($"public static {firelyResourceName}? Marshal_Resource({typeof(Resource).FullName}* resource)");
            builder.AppendLine("{");
            builder.IndentedAmount += 1;

            // need to get type
            string typeName = "type";

            builder.AppendLine($"if (resource == null) return null;");
            string getType = $"var {typeName} = *((ResourceType*)resource);";
            builder.AppendLine(getType);

            builder.AppendLine($"switch({typeName})");
            builder.AppendLine("{");

            builder.IndentedAmount += 1;
            foreach(var kvp in typeMap)
            {
                var baseType = kvp.Value.BaseType;
                while(baseType != typeof(Hl7.Fhir.Model.Resource) && baseType != typeof(System.Object))
                {
                    baseType = baseType.BaseType;
                }
                if (baseType != typeof(Hl7.Fhir.Model.Resource)) { continue; }

                string deserializeName = $"Marshal_{kvp.Key.Name}";
                builder.AppendLine($"case FHIR_Marshalling.ResourceType.{kvp.Key.Name}: return ({firelyResourceName}?){deserializeName}(({kvp.Key.FullName}*)resource);");
            }

            builder.IndentedAmount -= 1;
            builder.AppendLine("}");

            builder.AppendLine("throw new NotImplementedException();");

            builder.IndentedAmount -= 1;
            builder.AppendLine("}");
            return builder.ToString();
        }

        
        public static MappingInfo GetFirelyMappingInfo(Type nativeType)
        {
            Type firelyResourceType;
            if(NativeToFirely.TryGetValue(nativeType, out firelyResourceType) == false) {
                throw new NotImplementedException();
            }

            MappingInfo info = new MappingInfo();
            info.ClassName = firelyResourceType.Name;
            info.Members = new List<MemberMappingInfo>();
            info.FirelyType = firelyResourceType;

            var outputResource = Activator.CreateInstance(firelyResourceType);

            var nativeFields = nativeType.GetFields();
            int index = -1;
            foreach (var field in nativeFields)
            {
                var memberMapping = new MemberMappingInfo();
                index++;
                var workingField = field;

                FhirNameAttribute fhirAttribute = (FhirNameAttribute) workingField.GetCustomAttribute(typeof(FhirNameAttribute));

                // NOTE(agw): this is for extra fields like cmount and enum,
                // which we don't want to deal with directly
                if (fhirAttribute == null)
                {
                    continue;
                }

                string fhirName = ((FhirNameAttribute)fhirAttribute).FhirName;
                if (fhirName == "resourceType" || fhirName == "__field_extensions") continue;

                memberMapping.FhirName = fhirName;
                memberMapping.NativeField = field;

                var firelyProp = Firely_GetPropertyWithFhirElementName(outputResource.GetType(), fhirName);
                var firelyPropertyType = firelyProp.PropertyType;

                if(!IsNativePrimative(field.FieldType) && !field.FieldType.IsPointer)
                {
                     // Union
                    memberMapping.MemberType = MemberTypeEnum.Union;
                    memberMapping.NativeFieldName = field.Name;
                    memberMapping.NativeType = field.FieldType;

                    memberMapping.FirelyType = firelyPropertyType;
                    memberMapping.FirelyFieldName = firelyProp.Name;

                    var enumType = nativeFields[index + 1].FieldType;
                    var enumNames = enumType.GetEnumNames();

                    memberMapping.EnumType = enumType;
                    memberMapping.NativeEnumFieldName = nativeFields[index + 1].Name;
                    memberMapping.NativeEnumField = nativeFields[index + 1];


                    // Get possible native types and possible firely types...could be primative too
                    var unionNativeFields = field.FieldType.GetFields();
                    memberMapping.UnionEntries = new List<UnionEntry>();
                    int i = -1;
                    foreach(var unionField in unionNativeFields)
                    {
                        i++;

                        //Get matching Firely Type
                        Type unionFirelyType;
                        Type unionFieldType = unionField.FieldType ?? throw new Exception();
                        if(unionFieldType.IsPointer) { unionFieldType = unionFieldType.GetElementType();}

                        if(!NativeToFirely.TryGetValue(unionFieldType ?? throw new Exception(), out unionFirelyType))
                        {
                            var fhirType = unionField.GetCustomAttribute<NativeFhirTypeAttribute>();
                            if(fhirType == null) { throw new NotImplementedException(); }
                            unionFirelyType = fhirType.FirelyType;
                        }

                        UnionEntry entry = new UnionEntry();
                        entry.NativeUnionFieldName = unionField.Name;
                        entry.MemberEnumName = $"{enumType.FullName.Replace("+", ".")}.{enumNames[i]}";
                        entry.NativeType = unionFieldType;
                        entry.NativeField = unionField;
                        entry.FirelyType = unionFirelyType;
                        memberMapping.UnionEntries.Add(entry);
                    }
                }
                else if (IsNativePrimative(field.FieldType))
                {

                    if (firelyPropertyType.IsGenericType && field.FieldType == typeof(NullableString8))
                    {
                        memberMapping.MemberType = MemberTypeEnum.Enum;

                        var underlying = firelyPropertyType.GetGenericArguments()[0];
                        EnumEntry[] entries = GetEnumEntries(underlying);
                        memberMapping.EnumEntries = entries;
                    }
                    else
                    {
                        memberMapping.MemberType = MemberTypeEnum.Primative;
                    }

                    memberMapping.NativeFieldName = field.Name;
                    memberMapping.NativeType = field.FieldType;

                    memberMapping.FirelyFieldName = firelyProp.Name;
                    memberMapping.FirelyType = firelyPropertyType;
                }
                else if (field.FieldType.IsPointer)
                {
                    Type pointedToStructureType = workingField.FieldType.GetElementType();
                    // Is primative array
                    if (IsNativePrimative(pointedToStructureType))
                    {
                        if (firelyPropertyType.GetGenericArguments()[0].IsGenericType && field.FieldType == typeof(NullableString8*))
                        {
                            memberMapping.MemberType = MemberTypeEnum.Array_Of_Enum;

                            var underlying = firelyPropertyType.GetGenericArguments()[0].GetGenericArguments()[0];
                            EnumEntry[] entries = GetEnumEntries(underlying);
                            memberMapping.EnumEntries = entries;
                        }
                        else
                        {
                            memberMapping.MemberType = MemberTypeEnum.Array_Of_Primative;
                        }

                        memberMapping.NativeType = pointedToStructureType ?? throw new Exception();
                        memberMapping.NativeFieldName = field.Name;
                        memberMapping.NativeCountField = nativeFields[index - 1];

                        memberMapping.FirelyFieldName = firelyProp.Name;
                        memberMapping.FirelyType = firelyPropertyType;
                        if(firelyPropertyType.IsGenericType && firelyPropertyType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            memberMapping.FirelyType = firelyPropertyType.GetGenericArguments()[0];
                        }
                    }
                    // Is Array of Classes
                    else if (pointedToStructureType.UnderlyingSystemType.IsPointer)
                    {
                        memberMapping.MemberType = MemberTypeEnum.Array_Of_Classes;

                        memberMapping.NativeType = pointedToStructureType.UnderlyingSystemType.GetElementType() ?? throw new Exception();
                        memberMapping.NativeFieldName = field.Name;
                        memberMapping.NativeCountField = nativeFields[index - 1];

                        memberMapping.FirelyFieldName = firelyProp.Name;
                        memberMapping.FirelyType = firelyPropertyType;
                        if(firelyPropertyType.IsGenericType && firelyPropertyType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            memberMapping.FirelyType = firelyPropertyType.GetGenericArguments()[0];
                        }
                    }
                    // Class
                    else
                    {
                        memberMapping.MemberType = MemberTypeEnum.Class;

                        memberMapping.NativeType = pointedToStructureType;
                        memberMapping.NativeFieldName = field.Name;

                        memberMapping.FirelyFieldName = firelyProp.Name;
                        memberMapping.FirelyType = firelyPropertyType;
                    }
                }

                info.Members.Add(memberMapping);
            }

            return info;
        }

        public struct EnumEntry
        {
            public FieldInfo Field;
            public object Value;
            public EnumLiteralAttribute EnumLiteral;
        }
        public static EnumEntry[] GetEnumEntries(Type enumType)
        {
            var fields = enumType.GetFields();
            var enumValues = enumType.GetEnumValues();
            int i = -1;

            EnumEntry[] entries = new EnumEntry[enumValues.Length];
            foreach(var field in fields)
            {
                if (field.DeclaringType != enumType) continue;
                if (field.FieldType != enumType) continue;

                i++;

                EnumEntry entry = new EnumEntry
                {
                    Field = field,
                    Value = enumValues.GetValue(i) ?? throw new Exception(),
                    EnumLiteral = field.GetCustomAttribute<EnumLiteralAttribute>() ?? throw new Exception()
                };
                entries[i] = entry;
            }

            return entries;
        }

        private struct PropertyEntry
        {
            public PropertyInfo Info;
            public string FhirName;
        }
        private static Dictionary<Type, PropertyEntry[]> propertyInfoCache = new Dictionary<Type, PropertyEntry[]>();
        public static PropertyInfo? Firely_GetPropertyWithFhirElementName(Type type, string name)
        {
            PropertyEntry[] propertyInfos;
            if(!propertyInfoCache.TryGetValue(type, out propertyInfos))
            {
                PropertyInfo[] infos = type.GetProperties(); 
                List<PropertyEntry> entries = new List<PropertyEntry>();
                foreach(var info in infos)
                {
                    PropertyEntry entry = new PropertyEntry();
                    entry.Info = info;
                    //NOTE(agw): will we ever actually have more than one that matches?
                    var fhirElementAttributes = (IEnumerable<FhirElementAttribute>)info.GetCustomAttributes(typeof(Hl7.Fhir.Introspection.FhirElementAttribute));
                    foreach(var attribute in fhirElementAttributes)
                    {
                        var fhirElementAttribute = (FhirElementAttribute)attribute;
                        if (fhirElementAttribute == null) continue;
                        if (fhirElementAttribute.Since > Hl7.Fhir.Specification.FhirRelease.R4) continue;
                        entry.FhirName = fhirElementAttribute.Name;
                        entries.Add(entry);
                        // NOTE(agw)??
                        break;
                    }
                }
                propertyInfos = entries.ToArray();
                propertyInfoCache[type] = propertyInfos;
            }

            foreach (var entry in propertyInfos)
            {
                if (entry.FhirName == name) return entry.Info;
            }

            return null;
        }

        public static Dictionary<Type, Type> NativeToFirely = new Dictionary<Type, Type>() {
            { typeof(Resource), typeof(Hl7.Fhir.Model.Resource) },
            { typeof(Account), typeof(Hl7.Fhir.Model.Account) },
            { typeof(Account_Coverage), typeof(Hl7.Fhir.Model.Account.CoverageComponent) },
            { typeof(Account_Guarantor), typeof(Hl7.Fhir.Model.Account.GuarantorComponent) },
            { typeof(ActivityDefinition), typeof(Hl7.Fhir.Model.ActivityDefinition) },
            { typeof(ActivityDefinition_Participant), typeof(Hl7.Fhir.Model.ActivityDefinition.ParticipantComponent) },
            { typeof(ActivityDefinition_DynamicValue), typeof(Hl7.Fhir.Model.ActivityDefinition.DynamicValueComponent) },
            { typeof(AdverseEvent), typeof(Hl7.Fhir.Model.AdverseEvent) },
            { typeof(AdverseEvent_SuspectEntity), typeof(Hl7.Fhir.Model.AdverseEvent.SuspectEntityComponent) },
            { typeof(AdverseEvent_SuspectEntity_Causality), typeof(Hl7.Fhir.Model.AdverseEvent.CausalityComponent) },
            { typeof(AllergyIntolerance), typeof(Hl7.Fhir.Model.AllergyIntolerance) },
            { typeof(AllergyIntolerance_Reaction), typeof(Hl7.Fhir.Model.AllergyIntolerance.ReactionComponent) },
            { typeof(Appointment), typeof(Hl7.Fhir.Model.Appointment) },
            { typeof(Appointment_Participant), typeof(Hl7.Fhir.Model.Appointment.ParticipantComponent) },
            { typeof(AppointmentResponse), typeof(Hl7.Fhir.Model.AppointmentResponse) },
            { typeof(AuditEvent), typeof(Hl7.Fhir.Model.AuditEvent) },
            { typeof(AuditEvent_Agent), typeof(Hl7.Fhir.Model.AuditEvent.AgentComponent) },
            { typeof(AuditEvent_Agent_Network), typeof(Hl7.Fhir.Model.AuditEvent.NetworkComponent) },
            { typeof(AuditEvent_Source), typeof(Hl7.Fhir.Model.AuditEvent.SourceComponent) },
            { typeof(AuditEvent_Entity), typeof(Hl7.Fhir.Model.AuditEvent.EntityComponent) },
            { typeof(AuditEvent_Entity_Detail), typeof(Hl7.Fhir.Model.AuditEvent.DetailComponent) },
            { typeof(Basic), typeof(Hl7.Fhir.Model.Basic) },
            { typeof(Binary), typeof(Hl7.Fhir.Model.Binary) },
            { typeof(BiologicallyDerivedProduct), typeof(Hl7.Fhir.Model.BiologicallyDerivedProduct) },
            { typeof(BiologicallyDerivedProduct_Collection), typeof(Hl7.Fhir.Model.BiologicallyDerivedProduct.CollectionComponent) },
            { typeof(BiologicallyDerivedProduct_Processing), typeof(Hl7.Fhir.Model.BiologicallyDerivedProduct.ProcessingComponent) },
            { typeof(BiologicallyDerivedProduct_Manipulation), typeof(Hl7.Fhir.Model.BiologicallyDerivedProduct.ManipulationComponent) },
            { typeof(BiologicallyDerivedProduct_Storage), typeof(Hl7.Fhir.Model.BiologicallyDerivedProduct.StorageComponent) },
            { typeof(BodyStructure), typeof(Hl7.Fhir.Model.BodyStructure) },
            { typeof(Bundle), typeof(Hl7.Fhir.Model.Bundle) },
            { typeof(Bundle_Link), typeof(Hl7.Fhir.Model.Bundle.LinkComponent) },
            { typeof(Bundle_Entry), typeof(Hl7.Fhir.Model.Bundle.EntryComponent) },
            { typeof(Bundle_Entry_Search), typeof(Hl7.Fhir.Model.Bundle.SearchComponent) },
            { typeof(Bundle_Entry_Request), typeof(Hl7.Fhir.Model.Bundle.RequestComponent) },
            { typeof(Bundle_Entry_Response), typeof(Hl7.Fhir.Model.Bundle.ResponseComponent) },
            { typeof(CapabilityStatement), typeof(Hl7.Fhir.Model.CapabilityStatement) },
            { typeof(CapabilityStatement_Software), typeof(Hl7.Fhir.Model.CapabilityStatement.SoftwareComponent) },
            { typeof(CapabilityStatement_Implementation), typeof(Hl7.Fhir.Model.CapabilityStatement.ImplementationComponent) },
            { typeof(CapabilityStatement_Rest), typeof(Hl7.Fhir.Model.CapabilityStatement.RestComponent) },
            { typeof(CapabilityStatement_Rest_Security), typeof(Hl7.Fhir.Model.CapabilityStatement.SecurityComponent) },
            { typeof(CapabilityStatement_Rest_Resource), typeof(Hl7.Fhir.Model.CapabilityStatement.ResourceComponent) },
            { typeof(CapabilityStatement_Rest_Resource_Interaction), typeof(Hl7.Fhir.Model.CapabilityStatement.ResourceInteractionComponent) },
            { typeof(CapabilityStatement_Rest_Resource_SearchParam), typeof(Hl7.Fhir.Model.CapabilityStatement.SearchParamComponent) },
            { typeof(CapabilityStatement_Rest_Resource_Operation), typeof(Hl7.Fhir.Model.CapabilityStatement.OperationComponent) },
            { typeof(CapabilityStatement_Rest_Interaction), typeof(Hl7.Fhir.Model.CapabilityStatement.SystemInteractionComponent) },
            { typeof(CapabilityStatement_Messaging), typeof(Hl7.Fhir.Model.CapabilityStatement.MessagingComponent) },
            { typeof(CapabilityStatement_Messaging_Endpoint), typeof(Hl7.Fhir.Model.CapabilityStatement.EndpointComponent) },
            { typeof(CapabilityStatement_Messaging_SupportedMessage), typeof(Hl7.Fhir.Model.CapabilityStatement.SupportedMessageComponent) },
            { typeof(CapabilityStatement_Document), typeof(Hl7.Fhir.Model.CapabilityStatement.DocumentComponent) },
            { typeof(CarePlan), typeof(Hl7.Fhir.Model.CarePlan) },
            { typeof(CarePlan_Activity), typeof(Hl7.Fhir.Model.CarePlan.ActivityComponent) },
            { typeof(CarePlan_Activity_Detail), typeof(Hl7.Fhir.Model.CarePlan.DetailComponent) },
            { typeof(CareTeam), typeof(Hl7.Fhir.Model.CareTeam) },
            { typeof(CareTeam_Participant), typeof(Hl7.Fhir.Model.CareTeam.ParticipantComponent) },
            { typeof(CatalogEntry), typeof(Hl7.Fhir.Model.CatalogEntry) },
            { typeof(CatalogEntry_RelatedEntry), typeof(Hl7.Fhir.Model.CatalogEntry.RelatedEntryComponent) },
            { typeof(ChargeItem), typeof(Hl7.Fhir.Model.ChargeItem) },
            { typeof(ChargeItem_Performer), typeof(Hl7.Fhir.Model.ChargeItem.PerformerComponent) },
            { typeof(ChargeItemDefinition), typeof(Hl7.Fhir.Model.ChargeItemDefinition) },
            { typeof(ChargeItemDefinition_Applicability), typeof(Hl7.Fhir.Model.ChargeItemDefinition.ApplicabilityComponent) },
            { typeof(ChargeItemDefinition_PropertyGroup), typeof(Hl7.Fhir.Model.ChargeItemDefinition.PropertyGroupComponent) },
            { typeof(ChargeItemDefinition_PropertyGroup_PriceComponent), typeof(Hl7.Fhir.Model.ChargeItemDefinition.PriceComponentComponent) },
            { typeof(Claim), typeof(Hl7.Fhir.Model.Claim) },
            { typeof(Claim_Related), typeof(Hl7.Fhir.Model.Claim.RelatedClaimComponent) },
            { typeof(Claim_Payee), typeof(Hl7.Fhir.Model.Claim.PayeeComponent) },
            { typeof(Claim_CareTeam), typeof(Hl7.Fhir.Model.Claim.CareTeamComponent) },
            { typeof(Claim_SupportingInfo), typeof(Hl7.Fhir.Model.Claim.SupportingInformationComponent) },
            { typeof(Claim_Diagnosis), typeof(Hl7.Fhir.Model.Claim.DiagnosisComponent) },
            { typeof(Claim_Procedure), typeof(Hl7.Fhir.Model.Claim.ProcedureComponent) },
            { typeof(Claim_Insurance), typeof(Hl7.Fhir.Model.Claim.InsuranceComponent) },
            { typeof(Claim_Accident), typeof(Hl7.Fhir.Model.Claim.AccidentComponent) },
            { typeof(Claim_Item), typeof(Hl7.Fhir.Model.Claim.ItemComponent) },
            { typeof(Claim_Item_Detail), typeof(Hl7.Fhir.Model.Claim.DetailComponent) },
            { typeof(Claim_Item_Detail_SubDetail), typeof(Hl7.Fhir.Model.Claim.SubDetailComponent) },
            { typeof(ClaimResponse), typeof(Hl7.Fhir.Model.ClaimResponse) },
            { typeof(ClaimResponse_Item), typeof(Hl7.Fhir.Model.ClaimResponse.ItemComponent) },
            { typeof(ClaimResponse_Item_Adjudication), typeof(Hl7.Fhir.Model.ClaimResponse.AdjudicationComponent) },
            { typeof(ClaimResponse_Item_Detail), typeof(Hl7.Fhir.Model.ClaimResponse.ItemDetailComponent) },
            { typeof(ClaimResponse_Item_Detail_SubDetail), typeof(Hl7.Fhir.Model.ClaimResponse.SubDetailComponent) },
            { typeof(ClaimResponse_AddItem), typeof(Hl7.Fhir.Model.ClaimResponse.AddedItemComponent) },
            { typeof(ClaimResponse_AddItem_Detail), typeof(Hl7.Fhir.Model.ClaimResponse.AddedItemDetailComponent) },
            { typeof(ClaimResponse_AddItem_Detail_SubDetail), typeof(Hl7.Fhir.Model.ClaimResponse.AddedItemSubDetailComponent) },
            { typeof(ClaimResponse_Total), typeof(Hl7.Fhir.Model.ClaimResponse.TotalComponent) },
            { typeof(ClaimResponse_Payment), typeof(Hl7.Fhir.Model.ClaimResponse.PaymentComponent) },
            { typeof(ClaimResponse_ProcessNote), typeof(Hl7.Fhir.Model.ClaimResponse.NoteComponent) },
            { typeof(ClaimResponse_Insurance), typeof(Hl7.Fhir.Model.ClaimResponse.InsuranceComponent) },
            { typeof(ClaimResponse_Error), typeof(Hl7.Fhir.Model.ClaimResponse.ErrorComponent) },
            { typeof(ClinicalImpression), typeof(Hl7.Fhir.Model.ClinicalImpression) },
            { typeof(ClinicalImpression_Investigation), typeof(Hl7.Fhir.Model.ClinicalImpression.InvestigationComponent) },
            { typeof(ClinicalImpression_Finding), typeof(Hl7.Fhir.Model.ClinicalImpression.FindingComponent) },
            { typeof(CodeSystem), typeof(Hl7.Fhir.Model.CodeSystem) },
            { typeof(CodeSystem_Filter), typeof(Hl7.Fhir.Model.CodeSystem.FilterComponent) },
            { typeof(CodeSystem_Property), typeof(Hl7.Fhir.Model.CodeSystem.PropertyComponent) },
            { typeof(CodeSystem_Concept), typeof(Hl7.Fhir.Model.CodeSystem.ConceptDefinitionComponent) },
            { typeof(CodeSystem_Concept_Designation), typeof(Hl7.Fhir.Model.CodeSystem.DesignationComponent) },
            { typeof(CodeSystem_Concept_Property), typeof(Hl7.Fhir.Model.CodeSystem.ConceptPropertyComponent) },
            { typeof(Communication), typeof(Hl7.Fhir.Model.Communication) },
            { typeof(Communication_Payload), typeof(Hl7.Fhir.Model.Communication.PayloadComponent) },
            { typeof(CommunicationRequest), typeof(Hl7.Fhir.Model.CommunicationRequest) },
            { typeof(CommunicationRequest_Payload), typeof(Hl7.Fhir.Model.CommunicationRequest.PayloadComponent) },
            { typeof(CompartmentDefinition), typeof(Hl7.Fhir.Model.CompartmentDefinition) },
            { typeof(CompartmentDefinition_Resource), typeof(Hl7.Fhir.Model.CompartmentDefinition.ResourceComponent) },
            { typeof(Composition), typeof(Hl7.Fhir.Model.Composition) },
            { typeof(Composition_Attester), typeof(Hl7.Fhir.Model.Composition.AttesterComponent) },
            { typeof(Composition_RelatesTo), typeof(Hl7.Fhir.Model.Composition.RelatesToComponent) },
            { typeof(Composition_Event), typeof(Hl7.Fhir.Model.Composition.EventComponent) },
            { typeof(Composition_Section), typeof(Hl7.Fhir.Model.Composition.SectionComponent) },
            { typeof(ConceptMap), typeof(Hl7.Fhir.Model.ConceptMap) },
            { typeof(ConceptMap_Group), typeof(Hl7.Fhir.Model.ConceptMap.GroupComponent) },
            { typeof(ConceptMap_Group_Element), typeof(Hl7.Fhir.Model.ConceptMap.SourceElementComponent) },
            { typeof(ConceptMap_Group_Element_Target), typeof(Hl7.Fhir.Model.ConceptMap.TargetElementComponent) },
            { typeof(ConceptMap_Group_Element_Target_DependsOn), typeof(Hl7.Fhir.Model.ConceptMap.OtherElementComponent) },
            { typeof(ConceptMap_Group_Unmapped), typeof(Hl7.Fhir.Model.ConceptMap.UnmappedComponent) },
            { typeof(Condition), typeof(Hl7.Fhir.Model.Condition) },
            { typeof(Condition_Stage), typeof(Hl7.Fhir.Model.Condition.StageComponent) },
            { typeof(Condition_Evidence), typeof(Hl7.Fhir.Model.Condition.EvidenceComponent) },
            { typeof(Consent), typeof(Hl7.Fhir.Model.Consent) },
            { typeof(Consent_Policy), typeof(Hl7.Fhir.Model.Consent.PolicyComponent) },
            { typeof(Consent_Verification), typeof(Hl7.Fhir.Model.Consent.VerificationComponent) },
            { typeof(Consent_Provision), typeof(Hl7.Fhir.Model.Consent.provisionComponent) },
            { typeof(Consent_Provision_Actor), typeof(Hl7.Fhir.Model.Consent.provisionActorComponent) },
            { typeof(Consent_Provision_Data), typeof(Hl7.Fhir.Model.Consent.provisionDataComponent) },
            { typeof(Contract), typeof(Hl7.Fhir.Model.Contract) },
            { typeof(Contract_ContentDefinition), typeof(Hl7.Fhir.Model.Contract.ContentDefinitionComponent) },
            { typeof(Contract_Term), typeof(Hl7.Fhir.Model.Contract.TermComponent) },
            { typeof(Contract_Term_SecurityLabel), typeof(Hl7.Fhir.Model.Contract.SecurityLabelComponent) },
            { typeof(Contract_Term_Offer), typeof(Hl7.Fhir.Model.Contract.ContractOfferComponent) },
            { typeof(Contract_Term_Offer_Party), typeof(Hl7.Fhir.Model.Contract.ContractPartyComponent) },
            { typeof(Contract_Term_Offer_Answer), typeof(Hl7.Fhir.Model.Contract.AnswerComponent) },
            { typeof(Contract_Term_Asset), typeof(Hl7.Fhir.Model.Contract.ContractAssetComponent) },
            { typeof(Contract_Term_Asset_Context), typeof(Hl7.Fhir.Model.Contract.AssetContextComponent) },
            { typeof(Contract_Term_Asset_ValuedItem), typeof(Hl7.Fhir.Model.Contract.ValuedItemComponent) },
            { typeof(Contract_Term_Action), typeof(Hl7.Fhir.Model.Contract.ActionComponent) },
            { typeof(Contract_Term_Action_Subject), typeof(Hl7.Fhir.Model.Contract.ActionSubjectComponent) },
            { typeof(Contract_Signer), typeof(Hl7.Fhir.Model.Contract.SignatoryComponent) },
            { typeof(Contract_Friendly), typeof(Hl7.Fhir.Model.Contract.FriendlyLanguageComponent) },
            { typeof(Contract_Legal), typeof(Hl7.Fhir.Model.Contract.LegalLanguageComponent) },
            { typeof(Contract_Rule), typeof(Hl7.Fhir.Model.Contract.ComputableLanguageComponent) },
            { typeof(Coverage), typeof(Hl7.Fhir.Model.Coverage) },
            { typeof(Coverage_Class), typeof(Hl7.Fhir.Model.Coverage.ClassComponent) },
            { typeof(Coverage_CostToBeneficiary), typeof(Hl7.Fhir.Model.Coverage.CostToBeneficiaryComponent) },
            { typeof(Coverage_CostToBeneficiary_Exception), typeof(Hl7.Fhir.Model.Coverage.ExemptionComponent) },
            { typeof(CoverageEligibilityRequest), typeof(Hl7.Fhir.Model.CoverageEligibilityRequest) },
            { typeof(CoverageEligibilityRequest_SupportingInfo), typeof(Hl7.Fhir.Model.CoverageEligibilityRequest.SupportingInformationComponent) },
            { typeof(CoverageEligibilityRequest_Insurance), typeof(Hl7.Fhir.Model.CoverageEligibilityRequest.InsuranceComponent) },
            { typeof(CoverageEligibilityRequest_Item), typeof(Hl7.Fhir.Model.CoverageEligibilityRequest.DetailsComponent) },
            { typeof(CoverageEligibilityRequest_Item_Diagnosis), typeof(Hl7.Fhir.Model.CoverageEligibilityRequest.DiagnosisComponent) },
            { typeof(CoverageEligibilityResponse), typeof(Hl7.Fhir.Model.CoverageEligibilityResponse) },
            { typeof(CoverageEligibilityResponse_Insurance), typeof(Hl7.Fhir.Model.CoverageEligibilityResponse.InsuranceComponent) },
            { typeof(CoverageEligibilityResponse_Insurance_Item), typeof(Hl7.Fhir.Model.CoverageEligibilityResponse.ItemsComponent) },
            { typeof(CoverageEligibilityResponse_Insurance_Item_Benefit), typeof(Hl7.Fhir.Model.CoverageEligibilityResponse.BenefitComponent) },
            { typeof(CoverageEligibilityResponse_Error), typeof(Hl7.Fhir.Model.CoverageEligibilityResponse.ErrorsComponent) },
            { typeof(DetectedIssue), typeof(Hl7.Fhir.Model.DetectedIssue) },
            { typeof(DetectedIssue_Evidence), typeof(Hl7.Fhir.Model.DetectedIssue.EvidenceComponent) },
            { typeof(DetectedIssue_Mitigation), typeof(Hl7.Fhir.Model.DetectedIssue.MitigationComponent) },
            { typeof(Device), typeof(Hl7.Fhir.Model.Device) },
            { typeof(Device_UdiCarrier), typeof(Hl7.Fhir.Model.Device.UdiCarrierComponent) },
            { typeof(Device_DeviceName), typeof(Hl7.Fhir.Model.Device.DeviceNameComponent) },
            { typeof(Device_Specialization), typeof(Hl7.Fhir.Model.Device.SpecializationComponent) },
            { typeof(Device_Version), typeof(Hl7.Fhir.Model.Device.VersionComponent) },
            { typeof(Device_Property), typeof(Hl7.Fhir.Model.Device.PropertyComponent) },
            { typeof(DeviceDefinition), typeof(Hl7.Fhir.Model.DeviceDefinition) },
            { typeof(DeviceDefinition_UdiDeviceIdentifier), typeof(Hl7.Fhir.Model.DeviceDefinition.UdiDeviceIdentifierComponent) },
            { typeof(DeviceDefinition_DeviceName), typeof(Hl7.Fhir.Model.DeviceDefinition.DeviceNameComponent) },
            { typeof(DeviceDefinition_Specialization), typeof(Hl7.Fhir.Model.DeviceDefinition.SpecializationComponent) },
            { typeof(DeviceDefinition_Capability), typeof(Hl7.Fhir.Model.DeviceDefinition.CapabilityComponent) },
            { typeof(DeviceDefinition_Property), typeof(Hl7.Fhir.Model.DeviceDefinition.PropertyComponent) },
            { typeof(DeviceDefinition_Material), typeof(Hl7.Fhir.Model.DeviceDefinition.MaterialComponent) },
            { typeof(DeviceMetric), typeof(Hl7.Fhir.Model.DeviceMetric) },
            { typeof(DeviceMetric_Calibration), typeof(Hl7.Fhir.Model.DeviceMetric.CalibrationComponent) },
            { typeof(DeviceRequest), typeof(Hl7.Fhir.Model.DeviceRequest) },
            { typeof(DeviceRequest_Parameter), typeof(Hl7.Fhir.Model.DeviceRequest.ParameterComponent) },
            { typeof(DeviceUseStatement), typeof(Hl7.Fhir.Model.DeviceUseStatement) },
            { typeof(DiagnosticReport), typeof(Hl7.Fhir.Model.DiagnosticReport) },
            { typeof(DiagnosticReport_Media), typeof(Hl7.Fhir.Model.DiagnosticReport.MediaComponent) },
            { typeof(DocumentManifest), typeof(Hl7.Fhir.Model.DocumentManifest) },
            { typeof(DocumentManifest_Related), typeof(Hl7.Fhir.Model.DocumentManifest.RelatedComponent) },
            { typeof(DocumentReference), typeof(Hl7.Fhir.Model.DocumentReference) },
            { typeof(DocumentReference_RelatesTo), typeof(Hl7.Fhir.Model.DocumentReference.RelatesToComponent) },
            { typeof(DocumentReference_Content), typeof(Hl7.Fhir.Model.DocumentReference.ContentComponent) },
            { typeof(DocumentReference_Context), typeof(Hl7.Fhir.Model.DocumentReference.ContextComponent) },
            // NOTE(agw): abstract, cannot create
            //{ typeof(DomainResource), typeof(Hl7.Fhir.Model.DomainResource) },
            { typeof(EffectEvidenceSynthesis), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis) },
            { typeof(EffectEvidenceSynthesis_SampleSize), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.SampleSizeComponent) },
            { typeof(EffectEvidenceSynthesis_ResultsByExposure), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.ResultsByExposureComponent) },
            { typeof(EffectEvidenceSynthesis_EffectEstimate), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.EffectEstimateComponent) },
            { typeof(EffectEvidenceSynthesis_EffectEstimate_PrecisionEstimate), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.PrecisionEstimateComponent) },
            { typeof(EffectEvidenceSynthesis_Certainty), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.CertaintyComponent) },
            { typeof(EffectEvidenceSynthesis_Certainty_CertaintySubcomponent), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.CertaintySubcomponentComponent) },
            { typeof(Encounter), typeof(Hl7.Fhir.Model.Encounter) },
            { typeof(Encounter_StatusHistory), typeof(Hl7.Fhir.Model.Encounter.StatusHistoryComponent) },
            { typeof(Encounter_ClassHistory), typeof(Hl7.Fhir.Model.Encounter.ClassHistoryComponent) },
            { typeof(Encounter_Participant), typeof(Hl7.Fhir.Model.Encounter.ParticipantComponent) },
            { typeof(Encounter_Diagnosis), typeof(Hl7.Fhir.Model.Encounter.DiagnosisComponent) },
            { typeof(Encounter_Hospitalization), typeof(Hl7.Fhir.Model.Encounter.HospitalizationComponent) },
            { typeof(Encounter_Location), typeof(Hl7.Fhir.Model.Encounter.LocationComponent) },
            { typeof(Endpoint), typeof(Hl7.Fhir.Model.Endpoint) },
            { typeof(EnrollmentRequest), typeof(Hl7.Fhir.Model.EnrollmentRequest) },
            { typeof(EnrollmentResponse), typeof(Hl7.Fhir.Model.EnrollmentResponse) },
            { typeof(EpisodeOfCare), typeof(Hl7.Fhir.Model.EpisodeOfCare) },
            { typeof(EpisodeOfCare_StatusHistory), typeof(Hl7.Fhir.Model.EpisodeOfCare.StatusHistoryComponent) },
            { typeof(EpisodeOfCare_Diagnosis), typeof(Hl7.Fhir.Model.EpisodeOfCare.DiagnosisComponent) },
            { typeof(EventDefinition), typeof(Hl7.Fhir.Model.EventDefinition) },
            { typeof(Evidence), typeof(Hl7.Fhir.Model.Evidence) },
            { typeof(EvidenceVariable), typeof(Hl7.Fhir.Model.EvidenceVariable) },
            { typeof(EvidenceVariable_Characteristic), typeof(Hl7.Fhir.Model.EvidenceVariable.CharacteristicComponent) },
            { typeof(ExampleScenario), typeof(Hl7.Fhir.Model.ExampleScenario) },
            { typeof(ExampleScenario_Actor), typeof(Hl7.Fhir.Model.ExampleScenario.ActorComponent) },
            { typeof(ExampleScenario_Instance), typeof(Hl7.Fhir.Model.ExampleScenario.InstanceComponent) },
            { typeof(ExampleScenario_Instance_Version), typeof(Hl7.Fhir.Model.ExampleScenario.VersionComponent) },
            { typeof(ExampleScenario_Instance_ContainedInstance), typeof(Hl7.Fhir.Model.ExampleScenario.ContainedInstanceComponent) },
            { typeof(ExampleScenario_Process), typeof(Hl7.Fhir.Model.ExampleScenario.ProcessComponent) },
            { typeof(ExampleScenario_Process_Step), typeof(Hl7.Fhir.Model.ExampleScenario.StepComponent) },
            { typeof(ExampleScenario_Process_Step_Operation), typeof(Hl7.Fhir.Model.ExampleScenario.OperationComponent) },
            { typeof(ExampleScenario_Process_Step_Alternative), typeof(Hl7.Fhir.Model.ExampleScenario.AlternativeComponent) },
            { typeof(ExplanationOfBenefit), typeof(Hl7.Fhir.Model.ExplanationOfBenefit) },
            { typeof(ExplanationOfBenefit_Related), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.RelatedClaimComponent) },
            { typeof(ExplanationOfBenefit_Payee), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.PayeeComponent) },
            { typeof(ExplanationOfBenefit_CareTeam), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.CareTeamComponent) },
            { typeof(ExplanationOfBenefit_SupportingInfo), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.SupportingInformationComponent) },
            { typeof(ExplanationOfBenefit_Diagnosis), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.DiagnosisComponent) },
            { typeof(ExplanationOfBenefit_Procedure), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.ProcedureComponent) },
            { typeof(ExplanationOfBenefit_Insurance), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.InsuranceComponent) },
            { typeof(ExplanationOfBenefit_Accident), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.AccidentComponent) },
            { typeof(ExplanationOfBenefit_Item), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.ItemComponent) },
            { typeof(ExplanationOfBenefit_Item_Adjudication), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.AdjudicationComponent) },
            { typeof(ExplanationOfBenefit_Item_Detail), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.DetailComponent) },
            { typeof(ExplanationOfBenefit_Item_Detail_SubDetail), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.SubDetailComponent) },
            { typeof(ExplanationOfBenefit_AddItem), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.AddedItemComponent) },
            { typeof(ExplanationOfBenefit_AddItem_Detail), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.AddedItemDetailComponent) },
            { typeof(ExplanationOfBenefit_AddItem_Detail_SubDetail), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.AddedItemDetailSubDetailComponent) },
            { typeof(ExplanationOfBenefit_Total), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.TotalComponent) },
            { typeof(ExplanationOfBenefit_Payment), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.PaymentComponent) },
            { typeof(ExplanationOfBenefit_ProcessNote), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.NoteComponent) },
            { typeof(ExplanationOfBenefit_BenefitBalance), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.BenefitBalanceComponent) },
            { typeof(ExplanationOfBenefit_BenefitBalance_Financial), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.BenefitComponent) },
            { typeof(FamilyMemberHistory), typeof(Hl7.Fhir.Model.FamilyMemberHistory) },
            { typeof(FamilyMemberHistory_Condition), typeof(Hl7.Fhir.Model.FamilyMemberHistory.ConditionComponent) },
            { typeof(Flag), typeof(Hl7.Fhir.Model.Flag) },
            { typeof(Goal), typeof(Hl7.Fhir.Model.Goal) },
            { typeof(Goal_Target), typeof(Hl7.Fhir.Model.Goal.TargetComponent) },
            { typeof(GraphDefinition), typeof(Hl7.Fhir.Model.GraphDefinition) },
            { typeof(GraphDefinition_Link), typeof(Hl7.Fhir.Model.GraphDefinition.LinkComponent) },
            { typeof(GraphDefinition_Link_Target), typeof(Hl7.Fhir.Model.GraphDefinition.TargetComponent) },
            { typeof(GraphDefinition_Link_Target_Compartment), typeof(Hl7.Fhir.Model.GraphDefinition.CompartmentComponent) },
            { typeof(Group), typeof(Hl7.Fhir.Model.Group) },
            { typeof(Group_Characteristic), typeof(Hl7.Fhir.Model.Group.CharacteristicComponent) },
            { typeof(Group_Member), typeof(Hl7.Fhir.Model.Group.MemberComponent) },
            { typeof(GuidanceResponse), typeof(Hl7.Fhir.Model.GuidanceResponse) },
            { typeof(HealthcareService), typeof(Hl7.Fhir.Model.HealthcareService) },
            { typeof(HealthcareService_Eligibility), typeof(Hl7.Fhir.Model.HealthcareService.EligibilityComponent) },
            { typeof(HealthcareService_AvailableTime), typeof(Hl7.Fhir.Model.HealthcareService.AvailableTimeComponent) },
            { typeof(HealthcareService_NotAvailable), typeof(Hl7.Fhir.Model.HealthcareService.NotAvailableComponent) },
            { typeof(ImagingStudy), typeof(Hl7.Fhir.Model.ImagingStudy) },
            { typeof(ImagingStudy_Series), typeof(Hl7.Fhir.Model.ImagingStudy.SeriesComponent) },
            { typeof(ImagingStudy_Series_Performer), typeof(Hl7.Fhir.Model.ImagingStudy.PerformerComponent) },
            { typeof(ImagingStudy_Series_Instance), typeof(Hl7.Fhir.Model.ImagingStudy.InstanceComponent) },
            { typeof(Immunization), typeof(Hl7.Fhir.Model.Immunization) },
            { typeof(Immunization_Performer), typeof(Hl7.Fhir.Model.Immunization.PerformerComponent) },
            { typeof(Immunization_Education), typeof(Hl7.Fhir.Model.Immunization.EducationComponent) },
            { typeof(Immunization_Reaction), typeof(Hl7.Fhir.Model.Immunization.ReactionComponent) },
            { typeof(Immunization_ProtocolApplied), typeof(Hl7.Fhir.Model.Immunization.ProtocolAppliedComponent) },
            { typeof(ImmunizationEvaluation), typeof(Hl7.Fhir.Model.ImmunizationEvaluation) },
            { typeof(ImmunizationRecommendation), typeof(Hl7.Fhir.Model.ImmunizationRecommendation) },
            { typeof(ImmunizationRecommendation_Recommendation), typeof(Hl7.Fhir.Model.ImmunizationRecommendation.RecommendationComponent) },
            { typeof(ImmunizationRecommendation_Recommendation_DateCriterion), typeof(Hl7.Fhir.Model.ImmunizationRecommendation.DateCriterionComponent) },
            { typeof(ImplementationGuide), typeof(Hl7.Fhir.Model.ImplementationGuide) },
            { typeof(ImplementationGuide_DependsOn), typeof(Hl7.Fhir.Model.ImplementationGuide.DependsOnComponent) },
            { typeof(ImplementationGuide_Global), typeof(Hl7.Fhir.Model.ImplementationGuide.GlobalComponent) },
            { typeof(ImplementationGuide_Definition), typeof(Hl7.Fhir.Model.ImplementationGuide.DefinitionComponent) },
            { typeof(ImplementationGuide_Definition_Grouping), typeof(Hl7.Fhir.Model.ImplementationGuide.GroupingComponent) },
            { typeof(ImplementationGuide_Definition_Resource), typeof(Hl7.Fhir.Model.ImplementationGuide.ResourceComponent) },
            { typeof(ImplementationGuide_Definition_Page), typeof(Hl7.Fhir.Model.ImplementationGuide.PageComponent) },
            { typeof(ImplementationGuide_Definition_Parameter), typeof(Hl7.Fhir.Model.ImplementationGuide.ParameterComponent) },
            { typeof(ImplementationGuide_Definition_Template), typeof(Hl7.Fhir.Model.ImplementationGuide.TemplateComponent) },
            { typeof(ImplementationGuide_Manifest), typeof(Hl7.Fhir.Model.ImplementationGuide.ManifestComponent) },
            { typeof(ImplementationGuide_Manifest_Resource), typeof(Hl7.Fhir.Model.ImplementationGuide.ManifestResourceComponent) },
            { typeof(ImplementationGuide_Manifest_Page), typeof(Hl7.Fhir.Model.ImplementationGuide.ManifestPageComponent) },
            { typeof(InsurancePlan), typeof(Hl7.Fhir.Model.InsurancePlan) },
            { typeof(InsurancePlan_Contact), typeof(Hl7.Fhir.Model.InsurancePlan.ContactComponent) },
            { typeof(InsurancePlan_Coverage), typeof(Hl7.Fhir.Model.InsurancePlan.CoverageComponent) },
            { typeof(InsurancePlan_Coverage_Benefit), typeof(Hl7.Fhir.Model.InsurancePlan.CoverageBenefitComponent) },
            { typeof(InsurancePlan_Coverage_Benefit_Limit), typeof(Hl7.Fhir.Model.InsurancePlan.LimitComponent) },
            { typeof(InsurancePlan_Plan), typeof(Hl7.Fhir.Model.InsurancePlan.PlanComponent) },
            { typeof(InsurancePlan_Plan_GeneralCost), typeof(Hl7.Fhir.Model.InsurancePlan.GeneralCostComponent) },
            { typeof(InsurancePlan_Plan_SpecificCost), typeof(Hl7.Fhir.Model.InsurancePlan.SpecificCostComponent) },
            { typeof(InsurancePlan_Plan_SpecificCost_Benefit), typeof(Hl7.Fhir.Model.InsurancePlan.PlanBenefitComponent) },
            { typeof(InsurancePlan_Plan_SpecificCost_Benefit_Cost), typeof(Hl7.Fhir.Model.InsurancePlan.CostComponent) },
            { typeof(Invoice), typeof(Hl7.Fhir.Model.Invoice) },
            { typeof(Invoice_Participant), typeof(Hl7.Fhir.Model.Invoice.ParticipantComponent) },
            { typeof(Invoice_LineItem), typeof(Hl7.Fhir.Model.Invoice.LineItemComponent) },
            { typeof(Invoice_LineItem_PriceComponent), typeof(Hl7.Fhir.Model.Invoice.PriceComponentComponent) },
            { typeof(Library), typeof(Hl7.Fhir.Model.Library) },
            { typeof(Linkage), typeof(Hl7.Fhir.Model.Linkage) },
            { typeof(Linkage_Item), typeof(Hl7.Fhir.Model.Linkage.ItemComponent) },
            { typeof(List), typeof(Hl7.Fhir.Model.List) },
            { typeof(List_Entry), typeof(Hl7.Fhir.Model.List.EntryComponent) },
            { typeof(Location), typeof(Hl7.Fhir.Model.Location) },
            { typeof(Location_Position), typeof(Hl7.Fhir.Model.Location.PositionComponent) },
            { typeof(Location_HoursOfOperation), typeof(Hl7.Fhir.Model.Location.HoursOfOperationComponent) },
            { typeof(Measure), typeof(Hl7.Fhir.Model.Measure) },
            { typeof(Measure_Group), typeof(Hl7.Fhir.Model.Measure.GroupComponent) },
            { typeof(Measure_Group_Population), typeof(Hl7.Fhir.Model.Measure.PopulationComponent) },
            { typeof(Measure_Group_Stratifier), typeof(Hl7.Fhir.Model.Measure.StratifierComponent) },
            { typeof(Measure_Group_Stratifier_Component), typeof(Hl7.Fhir.Model.Measure.ComponentComponent) },
            { typeof(Measure_SupplementalData), typeof(Hl7.Fhir.Model.Measure.SupplementalDataComponent) },
            { typeof(MeasureReport), typeof(Hl7.Fhir.Model.MeasureReport) },
            { typeof(MeasureReport_Group), typeof(Hl7.Fhir.Model.MeasureReport.GroupComponent) },
            { typeof(MeasureReport_Group_Population), typeof(Hl7.Fhir.Model.MeasureReport.PopulationComponent) },
            { typeof(MeasureReport_Group_Stratifier), typeof(Hl7.Fhir.Model.MeasureReport.StratifierComponent) },
            { typeof(MeasureReport_Group_Stratifier_Stratum), typeof(Hl7.Fhir.Model.MeasureReport.StratifierGroupComponent) },
            { typeof(MeasureReport_Group_Stratifier_Stratum_Component), typeof(Hl7.Fhir.Model.MeasureReport.ComponentComponent) },
            { typeof(MeasureReport_Group_Stratifier_Stratum_Population), typeof(Hl7.Fhir.Model.MeasureReport.StratifierGroupPopulationComponent) },
            { typeof(Media), typeof(Hl7.Fhir.Model.Media) },
            { typeof(Medication), typeof(Hl7.Fhir.Model.Medication) },
            { typeof(Medication_Ingredient), typeof(Hl7.Fhir.Model.Medication.IngredientComponent) },
            { typeof(Medication_Batch), typeof(Hl7.Fhir.Model.Medication.BatchComponent) },
            { typeof(MedicationAdministration), typeof(Hl7.Fhir.Model.MedicationAdministration) },
            { typeof(MedicationAdministration_Performer), typeof(Hl7.Fhir.Model.MedicationAdministration.PerformerComponent) },
            { typeof(MedicationAdministration_Dosage), typeof(Hl7.Fhir.Model.MedicationAdministration.DosageComponent) },
            { typeof(MedicationDispense), typeof(Hl7.Fhir.Model.MedicationDispense) },
            { typeof(MedicationDispense_Performer), typeof(Hl7.Fhir.Model.MedicationDispense.PerformerComponent) },
            { typeof(MedicationDispense_Substitution), typeof(Hl7.Fhir.Model.MedicationDispense.SubstitutionComponent) },
            { typeof(MedicationKnowledge), typeof(Hl7.Fhir.Model.MedicationKnowledge) },
            { typeof(MedicationKnowledge_RelatedMedicationKnowledge), typeof(Hl7.Fhir.Model.MedicationKnowledge.RelatedMedicationKnowledgeComponent) },
            { typeof(MedicationKnowledge_Monograph), typeof(Hl7.Fhir.Model.MedicationKnowledge.MonographComponent) },
            { typeof(MedicationKnowledge_Ingredient), typeof(Hl7.Fhir.Model.MedicationKnowledge.IngredientComponent) },
            { typeof(MedicationKnowledge_Cost), typeof(Hl7.Fhir.Model.MedicationKnowledge.CostComponent) },
            { typeof(MedicationKnowledge_MonitoringProgram), typeof(Hl7.Fhir.Model.MedicationKnowledge.MonitoringProgramComponent) },
            { typeof(MedicationKnowledge_AdministrationGuidelines), typeof(Hl7.Fhir.Model.MedicationKnowledge.AdministrationGuidelinesComponent) },
            { typeof(MedicationKnowledge_AdministrationGuidelines_Dosage), typeof(Hl7.Fhir.Model.MedicationKnowledge.DosageComponent) },
            { typeof(MedicationKnowledge_AdministrationGuidelines_PatientCharacteristics), typeof(Hl7.Fhir.Model.MedicationKnowledge.PatientCharacteristicsComponent) },
            { typeof(MedicationKnowledge_MedicineClassification), typeof(Hl7.Fhir.Model.MedicationKnowledge.MedicineClassificationComponent) },
            { typeof(MedicationKnowledge_Packaging), typeof(Hl7.Fhir.Model.MedicationKnowledge.PackagingComponent) },
            { typeof(MedicationKnowledge_DrugCharacteristic), typeof(Hl7.Fhir.Model.MedicationKnowledge.DrugCharacteristicComponent) },
            { typeof(MedicationKnowledge_Regulatory), typeof(Hl7.Fhir.Model.MedicationKnowledge.RegulatoryComponent) },
            { typeof(MedicationKnowledge_Regulatory_Substitution), typeof(Hl7.Fhir.Model.MedicationKnowledge.SubstitutionComponent) },
            { typeof(MedicationKnowledge_Regulatory_Schedule), typeof(Hl7.Fhir.Model.MedicationKnowledge.ScheduleComponent) },
            { typeof(MedicationKnowledge_Regulatory_MaxDispense), typeof(Hl7.Fhir.Model.MedicationKnowledge.MaxDispenseComponent) },
            { typeof(MedicationKnowledge_Kinetics), typeof(Hl7.Fhir.Model.MedicationKnowledge.KineticsComponent) },
            { typeof(MedicationRequest), typeof(Hl7.Fhir.Model.MedicationRequest) },
            { typeof(MedicationRequest_DispenseRequest), typeof(Hl7.Fhir.Model.MedicationRequest.DispenseRequestComponent) },
            { typeof(MedicationRequest_DispenseRequest_InitialFill), typeof(Hl7.Fhir.Model.MedicationRequest.InitialFillComponent) },
            { typeof(MedicationRequest_Substitution), typeof(Hl7.Fhir.Model.MedicationRequest.SubstitutionComponent) },
            { typeof(MedicationStatement), typeof(Hl7.Fhir.Model.MedicationStatement) },
            { typeof(MedicinalProduct), typeof(Hl7.Fhir.Model.MedicinalProduct) },
            { typeof(MedicinalProduct_Name), typeof(Hl7.Fhir.Model.MedicinalProduct.NameComponent) },
            { typeof(MedicinalProduct_Name_NamePart), typeof(Hl7.Fhir.Model.MedicinalProduct.NamePartComponent) },
            { typeof(MedicinalProduct_Name_CountryLanguage), typeof(Hl7.Fhir.Model.MedicinalProduct.CountryLanguageComponent) },
            { typeof(MedicinalProduct_ManufacturingBusinessOperation), typeof(Hl7.Fhir.Model.MedicinalProduct.ManufacturingBusinessOperationComponent) },
            { typeof(MedicinalProduct_SpecialDesignation), typeof(Hl7.Fhir.Model.MedicinalProduct.SpecialDesignationComponent) },
            { typeof(MedicinalProductAuthorization), typeof(Hl7.Fhir.Model.MedicinalProductAuthorization) },
            { typeof(MedicinalProductAuthorization_JurisdictionalAuthorization), typeof(Hl7.Fhir.Model.MedicinalProductAuthorization.JurisdictionalAuthorizationComponent) },
            { typeof(MedicinalProductAuthorization_Procedure), typeof(Hl7.Fhir.Model.MedicinalProductAuthorization.ProcedureComponent) },
            { typeof(MedicinalProductContraindication), typeof(Hl7.Fhir.Model.MedicinalProductContraindication) },
            { typeof(MedicinalProductContraindication_OtherTherapy), typeof(Hl7.Fhir.Model.MedicinalProductContraindication.OtherTherapyComponent) },
            { typeof(MedicinalProductIndication), typeof(Hl7.Fhir.Model.MedicinalProductIndication) },
            { typeof(MedicinalProductIndication_OtherTherapy), typeof(Hl7.Fhir.Model.MedicinalProductIndication.OtherTherapyComponent) },
            { typeof(MedicinalProductIngredient), typeof(Hl7.Fhir.Model.MedicinalProductIngredient) },
            { typeof(MedicinalProductIngredient_SpecifiedSubstance), typeof(Hl7.Fhir.Model.MedicinalProductIngredient.SpecifiedSubstanceComponent) },
            { typeof(MedicinalProductIngredient_SpecifiedSubstance_Strength), typeof(Hl7.Fhir.Model.MedicinalProductIngredient.StrengthComponent) },
            { typeof(MedicinalProductIngredient_SpecifiedSubstance_Strength_ReferenceStrength), typeof(Hl7.Fhir.Model.MedicinalProductIngredient.ReferenceStrengthComponent) },
            { typeof(MedicinalProductIngredient_Substance), typeof(Hl7.Fhir.Model.MedicinalProductIngredient.SubstanceComponent) },
            { typeof(MedicinalProductInteraction), typeof(Hl7.Fhir.Model.MedicinalProductInteraction) },
            { typeof(MedicinalProductInteraction_Interactant), typeof(Hl7.Fhir.Model.MedicinalProductInteraction.InteractantComponent) },
            { typeof(MedicinalProductManufactured), typeof(Hl7.Fhir.Model.MedicinalProductManufactured) },
            { typeof(MedicinalProductPackaged), typeof(Hl7.Fhir.Model.MedicinalProductPackaged) },
            { typeof(MedicinalProductPackaged_BatchIdentifier), typeof(Hl7.Fhir.Model.MedicinalProductPackaged.BatchIdentifierComponent) },
            { typeof(MedicinalProductPackaged_PackageItem), typeof(Hl7.Fhir.Model.MedicinalProductPackaged.PackageItemComponent) },
            { typeof(MedicinalProductPharmaceutical), typeof(Hl7.Fhir.Model.MedicinalProductPharmaceutical) },
            { typeof(MedicinalProductPharmaceutical_Characteristics), typeof(Hl7.Fhir.Model.MedicinalProductPharmaceutical.CharacteristicsComponent) },
            { typeof(MedicinalProductPharmaceutical_RouteOfAdministration), typeof(Hl7.Fhir.Model.MedicinalProductPharmaceutical.RouteOfAdministrationComponent) },
            { typeof(MedicinalProductPharmaceutical_RouteOfAdministration_TargetSpecies), typeof(Hl7.Fhir.Model.MedicinalProductPharmaceutical.TargetSpeciesComponent) },
            { typeof(MedicinalProductPharmaceutical_RouteOfAdministration_TargetSpecies_WithdrawalPeriod), typeof(Hl7.Fhir.Model.MedicinalProductPharmaceutical.WithdrawalPeriodComponent) },
            { typeof(MedicinalProductUndesirableEffect), typeof(Hl7.Fhir.Model.MedicinalProductUndesirableEffect) },
            { typeof(MessageDefinition), typeof(Hl7.Fhir.Model.MessageDefinition) },
            { typeof(MessageDefinition_Focus), typeof(Hl7.Fhir.Model.MessageDefinition.FocusComponent) },
            { typeof(MessageDefinition_AllowedResponse), typeof(Hl7.Fhir.Model.MessageDefinition.AllowedResponseComponent) },
            { typeof(MessageHeader), typeof(Hl7.Fhir.Model.MessageHeader) },
            { typeof(MessageHeader_Destination), typeof(Hl7.Fhir.Model.MessageHeader.MessageDestinationComponent) },
            { typeof(MessageHeader_Source), typeof(Hl7.Fhir.Model.MessageHeader.MessageSourceComponent) },
            { typeof(MessageHeader_Response), typeof(Hl7.Fhir.Model.MessageHeader.ResponseComponent) },
            { typeof(MolecularSequence), typeof(Hl7.Fhir.Model.MolecularSequence) },
            { typeof(MolecularSequence_ReferenceSeq), typeof(Hl7.Fhir.Model.MolecularSequence.ReferenceSeqComponent) },
            { typeof(MolecularSequence_Variant), typeof(Hl7.Fhir.Model.MolecularSequence.VariantComponent) },
            { typeof(MolecularSequence_Quality), typeof(Hl7.Fhir.Model.MolecularSequence.QualityComponent) },
            { typeof(MolecularSequence_Quality_Roc), typeof(Hl7.Fhir.Model.MolecularSequence.RocComponent) },
            { typeof(MolecularSequence_Repository), typeof(Hl7.Fhir.Model.MolecularSequence.RepositoryComponent) },
            { typeof(MolecularSequence_StructureVariant), typeof(Hl7.Fhir.Model.MolecularSequence.StructureVariantComponent) },
            { typeof(MolecularSequence_StructureVariant_Outer), typeof(Hl7.Fhir.Model.MolecularSequence.OuterComponent) },
            { typeof(MolecularSequence_StructureVariant_Inner), typeof(Hl7.Fhir.Model.MolecularSequence.InnerComponent) },
            { typeof(NamingSystem), typeof(Hl7.Fhir.Model.NamingSystem) },
            { typeof(NamingSystem_UniqueId), typeof(Hl7.Fhir.Model.NamingSystem.UniqueIdComponent) },
            { typeof(NutritionOrder), typeof(Hl7.Fhir.Model.NutritionOrder) },
            { typeof(NutritionOrder_OralDiet), typeof(Hl7.Fhir.Model.NutritionOrder.OralDietComponent) },
            { typeof(NutritionOrder_OralDiet_Nutrient), typeof(Hl7.Fhir.Model.NutritionOrder.NutrientComponent) },
            { typeof(NutritionOrder_OralDiet_Texture), typeof(Hl7.Fhir.Model.NutritionOrder.TextureComponent) },
            { typeof(NutritionOrder_Supplement), typeof(Hl7.Fhir.Model.NutritionOrder.SupplementComponent) },
            { typeof(NutritionOrder_EnteralFormula), typeof(Hl7.Fhir.Model.NutritionOrder.EnteralFormulaComponent) },
            { typeof(NutritionOrder_EnteralFormula_Administration), typeof(Hl7.Fhir.Model.NutritionOrder.AdministrationComponent) },
            { typeof(Observation), typeof(Hl7.Fhir.Model.Observation) },
            { typeof(Observation_ReferenceRange), typeof(Hl7.Fhir.Model.Observation.ReferenceRangeComponent) },
            { typeof(Observation_Component), typeof(Hl7.Fhir.Model.Observation.ComponentComponent) },
            { typeof(ObservationDefinition), typeof(Hl7.Fhir.Model.ObservationDefinition) },
            { typeof(ObservationDefinition_QuantitativeDetails), typeof(Hl7.Fhir.Model.ObservationDefinition.QuantitativeDetailsComponent) },
            { typeof(ObservationDefinition_QualifiedInterval), typeof(Hl7.Fhir.Model.ObservationDefinition.QualifiedIntervalComponent) },
            { typeof(OperationDefinition), typeof(Hl7.Fhir.Model.OperationDefinition) },
            { typeof(OperationDefinition_Parameter), typeof(Hl7.Fhir.Model.OperationDefinition.ParameterComponent) },
            { typeof(OperationDefinition_Parameter_Binding), typeof(Hl7.Fhir.Model.OperationDefinition.BindingComponent) },
            { typeof(OperationDefinition_Parameter_ReferencedFrom), typeof(Hl7.Fhir.Model.OperationDefinition.ReferencedFromComponent) },
            { typeof(OperationDefinition_Overload), typeof(Hl7.Fhir.Model.OperationDefinition.OverloadComponent) },
            { typeof(OperationOutcome), typeof(Hl7.Fhir.Model.OperationOutcome) },
            { typeof(OperationOutcome_Issue), typeof(Hl7.Fhir.Model.OperationOutcome.IssueComponent) },
            { typeof(Organization), typeof(Hl7.Fhir.Model.Organization) },
            { typeof(Organization_Contact), typeof(Hl7.Fhir.Model.Organization.ContactComponent) },
            { typeof(OrganizationAffiliation), typeof(Hl7.Fhir.Model.OrganizationAffiliation) },
            { typeof(Parameters), typeof(Hl7.Fhir.Model.Parameters) },
            { typeof(Parameters_Parameter), typeof(Hl7.Fhir.Model.Parameters.ParameterComponent) },
            { typeof(Patient), typeof(Hl7.Fhir.Model.Patient) },
            { typeof(Patient_Contact), typeof(Hl7.Fhir.Model.Patient.ContactComponent) },
            { typeof(Patient_Communication), typeof(Hl7.Fhir.Model.Patient.CommunicationComponent) },
            { typeof(Patient_Link), typeof(Hl7.Fhir.Model.Patient.LinkComponent) },
            { typeof(PaymentNotice), typeof(Hl7.Fhir.Model.PaymentNotice) },
            { typeof(PaymentReconciliation), typeof(Hl7.Fhir.Model.PaymentReconciliation) },
            { typeof(PaymentReconciliation_Detail), typeof(Hl7.Fhir.Model.PaymentReconciliation.DetailsComponent) },
            { typeof(PaymentReconciliation_ProcessNote), typeof(Hl7.Fhir.Model.PaymentReconciliation.NotesComponent) },
            { typeof(Person), typeof(Hl7.Fhir.Model.Person) },
            { typeof(Person_Link), typeof(Hl7.Fhir.Model.Person.LinkComponent) },
            { typeof(PlanDefinition), typeof(Hl7.Fhir.Model.PlanDefinition) },
            { typeof(PlanDefinition_Goal), typeof(Hl7.Fhir.Model.PlanDefinition.GoalComponent) },
            { typeof(PlanDefinition_Goal_Target), typeof(Hl7.Fhir.Model.PlanDefinition.TargetComponent) },
            { typeof(PlanDefinition_Action), typeof(Hl7.Fhir.Model.PlanDefinition.ActionComponent) },
            { typeof(PlanDefinition_Action_Condition), typeof(Hl7.Fhir.Model.PlanDefinition.ConditionComponent) },
            { typeof(PlanDefinition_Action_RelatedAction), typeof(Hl7.Fhir.Model.PlanDefinition.RelatedActionComponent) },
            { typeof(PlanDefinition_Action_Participant), typeof(Hl7.Fhir.Model.PlanDefinition.ParticipantComponent) },
            { typeof(PlanDefinition_Action_DynamicValue), typeof(Hl7.Fhir.Model.PlanDefinition.DynamicValueComponent) },
            { typeof(Practitioner), typeof(Hl7.Fhir.Model.Practitioner) },
            { typeof(Practitioner_Qualification), typeof(Hl7.Fhir.Model.Practitioner.QualificationComponent) },
            { typeof(PractitionerRole), typeof(Hl7.Fhir.Model.PractitionerRole) },
            { typeof(PractitionerRole_AvailableTime), typeof(Hl7.Fhir.Model.PractitionerRole.AvailableTimeComponent) },
            { typeof(PractitionerRole_NotAvailable), typeof(Hl7.Fhir.Model.PractitionerRole.NotAvailableComponent) },
            { typeof(Procedure), typeof(Hl7.Fhir.Model.Procedure) },
            { typeof(Procedure_Performer), typeof(Hl7.Fhir.Model.Procedure.PerformerComponent) },
            { typeof(Procedure_FocalDevice), typeof(Hl7.Fhir.Model.Procedure.FocalDeviceComponent) },
            { typeof(Provenance), typeof(Hl7.Fhir.Model.Provenance) },
            { typeof(Provenance_Agent), typeof(Hl7.Fhir.Model.Provenance.AgentComponent) },
            { typeof(Provenance_Entity), typeof(Hl7.Fhir.Model.Provenance.EntityComponent) },
            { typeof(Questionnaire), typeof(Hl7.Fhir.Model.Questionnaire) },
            { typeof(Questionnaire_Item), typeof(Hl7.Fhir.Model.Questionnaire.ItemComponent) },
            { typeof(Questionnaire_Item_EnableWhen), typeof(Hl7.Fhir.Model.Questionnaire.EnableWhenComponent) },
            { typeof(Questionnaire_Item_AnswerOption), typeof(Hl7.Fhir.Model.Questionnaire.AnswerOptionComponent) },
            { typeof(Questionnaire_Item_Initial), typeof(Hl7.Fhir.Model.Questionnaire.InitialComponent) },
            { typeof(QuestionnaireResponse), typeof(Hl7.Fhir.Model.QuestionnaireResponse) },
            { typeof(QuestionnaireResponse_Item), typeof(Hl7.Fhir.Model.QuestionnaireResponse.ItemComponent) },
            { typeof(QuestionnaireResponse_Item_Answer), typeof(Hl7.Fhir.Model.QuestionnaireResponse.AnswerComponent) },
            { typeof(RelatedPerson), typeof(Hl7.Fhir.Model.RelatedPerson) },
            { typeof(RelatedPerson_Communication), typeof(Hl7.Fhir.Model.RelatedPerson.CommunicationComponent) },
            { typeof(RequestGroup), typeof(Hl7.Fhir.Model.RequestGroup) },
            { typeof(RequestGroup_Action), typeof(Hl7.Fhir.Model.RequestGroup.ActionComponent) },
            { typeof(RequestGroup_Action_Condition), typeof(Hl7.Fhir.Model.RequestGroup.ConditionComponent) },
            { typeof(RequestGroup_Action_RelatedAction), typeof(Hl7.Fhir.Model.RequestGroup.RelatedActionComponent) },
            { typeof(ResearchDefinition), typeof(Hl7.Fhir.Model.ResearchDefinition) },
            { typeof(ResearchElementDefinition), typeof(Hl7.Fhir.Model.ResearchElementDefinition) },
            { typeof(ResearchElementDefinition_Characteristic), typeof(Hl7.Fhir.Model.ResearchElementDefinition.CharacteristicComponent) },
            { typeof(ResearchStudy), typeof(Hl7.Fhir.Model.ResearchStudy) },
            { typeof(ResearchStudy_Arm), typeof(Hl7.Fhir.Model.ResearchStudy.ArmComponent) },
            { typeof(ResearchStudy_Objective), typeof(Hl7.Fhir.Model.ResearchStudy.ObjectiveComponent) },
            { typeof(ResearchSubject), typeof(Hl7.Fhir.Model.ResearchSubject) },
            { typeof(RiskAssessment), typeof(Hl7.Fhir.Model.RiskAssessment) },
            { typeof(RiskAssessment_Prediction), typeof(Hl7.Fhir.Model.RiskAssessment.PredictionComponent) },
            { typeof(RiskEvidenceSynthesis), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis) },
            { typeof(RiskEvidenceSynthesis_SampleSize), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis.SampleSizeComponent) },
            { typeof(RiskEvidenceSynthesis_RiskEstimate), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis.RiskEstimateComponent) },
            { typeof(RiskEvidenceSynthesis_RiskEstimate_PrecisionEstimate), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis.PrecisionEstimateComponent) },
            { typeof(RiskEvidenceSynthesis_Certainty), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis.CertaintyComponent) },
            { typeof(RiskEvidenceSynthesis_Certainty_CertaintySubcomponent), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis.CertaintySubcomponentComponent) },
            { typeof(Schedule), typeof(Hl7.Fhir.Model.Schedule) },
            { typeof(SearchParameter), typeof(Hl7.Fhir.Model.SearchParameter) },
            { typeof(SearchParameter_Component), typeof(Hl7.Fhir.Model.SearchParameter.ComponentComponent) },
            { typeof(ServiceRequest), typeof(Hl7.Fhir.Model.ServiceRequest) },
            { typeof(Slot), typeof(Hl7.Fhir.Model.Slot) },
            { typeof(Specimen), typeof(Hl7.Fhir.Model.Specimen) },
            { typeof(Specimen_Collection), typeof(Hl7.Fhir.Model.Specimen.CollectionComponent) },
            { typeof(Specimen_Processing), typeof(Hl7.Fhir.Model.Specimen.ProcessingComponent) },
            { typeof(Specimen_Container), typeof(Hl7.Fhir.Model.Specimen.ContainerComponent) },
            { typeof(SpecimenDefinition), typeof(Hl7.Fhir.Model.SpecimenDefinition) },
            { typeof(SpecimenDefinition_TypeTested), typeof(Hl7.Fhir.Model.SpecimenDefinition.TypeTestedComponent) },
            { typeof(SpecimenDefinition_TypeTested_Container), typeof(Hl7.Fhir.Model.SpecimenDefinition.ContainerComponent) },
            { typeof(SpecimenDefinition_TypeTested_Container_Additive), typeof(Hl7.Fhir.Model.SpecimenDefinition.AdditiveComponent) },
            { typeof(SpecimenDefinition_TypeTested_Handling), typeof(Hl7.Fhir.Model.SpecimenDefinition.HandlingComponent) },
            { typeof(StructureDefinition), typeof(Hl7.Fhir.Model.StructureDefinition) },
            { typeof(StructureDefinition_Mapping), typeof(Hl7.Fhir.Model.StructureDefinition.MappingComponent) },
            { typeof(StructureDefinition_Context), typeof(Hl7.Fhir.Model.StructureDefinition.ContextComponent) },
            { typeof(StructureDefinition_Snapshot), typeof(Hl7.Fhir.Model.StructureDefinition.SnapshotComponent) },
            { typeof(StructureDefinition_Differential), typeof(Hl7.Fhir.Model.StructureDefinition.DifferentialComponent) },
            { typeof(StructureMap), typeof(Hl7.Fhir.Model.StructureMap) },
            { typeof(StructureMap_Structure), typeof(Hl7.Fhir.Model.StructureMap.StructureComponent) },
            { typeof(StructureMap_Group), typeof(Hl7.Fhir.Model.StructureMap.GroupComponent) },
            { typeof(StructureMap_Group_Input), typeof(Hl7.Fhir.Model.StructureMap.InputComponent) },
            { typeof(StructureMap_Group_Rule), typeof(Hl7.Fhir.Model.StructureMap.RuleComponent) },
            { typeof(StructureMap_Group_Rule_Source), typeof(Hl7.Fhir.Model.StructureMap.SourceComponent) },
            { typeof(StructureMap_Group_Rule_Target), typeof(Hl7.Fhir.Model.StructureMap.TargetComponent) },
            { typeof(StructureMap_Group_Rule_Target_Parameter), typeof(Hl7.Fhir.Model.StructureMap.ParameterComponent) },
            { typeof(StructureMap_Group_Rule_Dependent), typeof(Hl7.Fhir.Model.StructureMap.DependentComponent) },
            { typeof(Subscription), typeof(Hl7.Fhir.Model.Subscription) },
            { typeof(Subscription_Channel), typeof(Hl7.Fhir.Model.Subscription.ChannelComponent) },
            { typeof(Substance), typeof(Hl7.Fhir.Model.Substance) },
            { typeof(Substance_Instance), typeof(Hl7.Fhir.Model.Substance.InstanceComponent) },
            { typeof(Substance_Ingredient), typeof(Hl7.Fhir.Model.Substance.IngredientComponent) },
            { typeof(SubstanceNucleicAcid), typeof(Hl7.Fhir.Model.SubstanceNucleicAcid) },
            { typeof(SubstanceNucleicAcid_Subunit), typeof(Hl7.Fhir.Model.SubstanceNucleicAcid.SubunitComponent) },
            { typeof(SubstanceNucleicAcid_Subunit_Linkage), typeof(Hl7.Fhir.Model.SubstanceNucleicAcid.LinkageComponent) },
            { typeof(SubstanceNucleicAcid_Subunit_Sugar), typeof(Hl7.Fhir.Model.SubstanceNucleicAcid.SugarComponent) },
            { typeof(SubstancePolymer), typeof(Hl7.Fhir.Model.SubstancePolymer) },
            { typeof(SubstancePolymer_MonomerSet), typeof(Hl7.Fhir.Model.SubstancePolymer.MonomerSetComponent) },
            { typeof(SubstancePolymer_MonomerSet_StartingMaterial), typeof(Hl7.Fhir.Model.SubstancePolymer.StartingMaterialComponent) },
            { typeof(SubstancePolymer_Repeat), typeof(Hl7.Fhir.Model.SubstancePolymer.RepeatComponent) },
            { typeof(SubstancePolymer_Repeat_RepeatUnit), typeof(Hl7.Fhir.Model.SubstancePolymer.RepeatUnitComponent) },
            { typeof(SubstancePolymer_Repeat_RepeatUnit_DegreeOfPolymerisation), typeof(Hl7.Fhir.Model.SubstancePolymer.DegreeOfPolymerisationComponent) },
            { typeof(SubstancePolymer_Repeat_RepeatUnit_StructuralRepresentation), typeof(Hl7.Fhir.Model.SubstancePolymer.StructuralRepresentationComponent) },
            { typeof(SubstanceProtein), typeof(Hl7.Fhir.Model.SubstanceProtein) },
            { typeof(SubstanceProtein_Subunit), typeof(Hl7.Fhir.Model.SubstanceProtein.SubunitComponent) },
            { typeof(SubstanceReferenceInformation), typeof(Hl7.Fhir.Model.SubstanceReferenceInformation) },
            { typeof(SubstanceReferenceInformation_Gene), typeof(Hl7.Fhir.Model.SubstanceReferenceInformation.GeneComponent) },
            { typeof(SubstanceReferenceInformation_GeneElement), typeof(Hl7.Fhir.Model.SubstanceReferenceInformation.GeneElementComponent) },
            { typeof(SubstanceReferenceInformation_Classification), typeof(Hl7.Fhir.Model.SubstanceReferenceInformation.ClassificationComponent) },
            { typeof(SubstanceReferenceInformation_Target), typeof(Hl7.Fhir.Model.SubstanceReferenceInformation.TargetComponent) },
            { typeof(SubstanceSourceMaterial), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial) },
            { typeof(SubstanceSourceMaterial_FractionDescription), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.FractionDescriptionComponent) },
            { typeof(SubstanceSourceMaterial_Organism), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.OrganismComponent) },
            { typeof(SubstanceSourceMaterial_Organism_Author), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.AuthorComponent) },
            { typeof(SubstanceSourceMaterial_Organism_Hybrid), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.HybridComponent) },
            { typeof(SubstanceSourceMaterial_Organism_OrganismGeneral), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.OrganismGeneralComponent) },
            { typeof(SubstanceSourceMaterial_PartDescription), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.PartDescriptionComponent) },
            { typeof(SubstanceSpecification), typeof(Hl7.Fhir.Model.SubstanceSpecification) },
            { typeof(SubstanceSpecification_Moiety), typeof(Hl7.Fhir.Model.SubstanceSpecification.MoietyComponent) },
            { typeof(SubstanceSpecification_Property), typeof(Hl7.Fhir.Model.SubstanceSpecification.PropertyComponent) },
            { typeof(SubstanceSpecification_Structure), typeof(Hl7.Fhir.Model.SubstanceSpecification.StructureComponent) },
            { typeof(SubstanceSpecification_Structure_Isotope), typeof(Hl7.Fhir.Model.SubstanceSpecification.IsotopeComponent) },
            { typeof(SubstanceSpecification_Structure_Isotope_MolecularWeight), typeof(Hl7.Fhir.Model.SubstanceSpecification.MolecularWeightComponent) },
            { typeof(SubstanceSpecification_Structure_Representation), typeof(Hl7.Fhir.Model.SubstanceSpecification.RepresentationComponent) },
            { typeof(SubstanceSpecification_Code), typeof(Hl7.Fhir.Model.SubstanceSpecification.CodeComponent) },
            { typeof(SubstanceSpecification_Name), typeof(Hl7.Fhir.Model.SubstanceSpecification.NameComponent) },
            { typeof(SubstanceSpecification_Name_Official), typeof(Hl7.Fhir.Model.SubstanceSpecification.OfficialComponent) },
            { typeof(SubstanceSpecification_Relationship), typeof(Hl7.Fhir.Model.SubstanceSpecification.RelationshipComponent) },
            { typeof(SupplyDelivery), typeof(Hl7.Fhir.Model.SupplyDelivery) },
            { typeof(SupplyDelivery_SuppliedItem), typeof(Hl7.Fhir.Model.SupplyDelivery.SuppliedItemComponent) },
            { typeof(SupplyRequest), typeof(Hl7.Fhir.Model.SupplyRequest) },
            { typeof(SupplyRequest_Parameter), typeof(Hl7.Fhir.Model.SupplyRequest.ParameterComponent) },
            { typeof(Task), typeof(Hl7.Fhir.Model.Task) },
            { typeof(Task_Restriction), typeof(Hl7.Fhir.Model.Task.RestrictionComponent) },
            { typeof(Task_Input), typeof(Hl7.Fhir.Model.Task.ParameterComponent) },
            { typeof(Task_Output), typeof(Hl7.Fhir.Model.Task.OutputComponent) },
            { typeof(TerminologyCapabilities), typeof(Hl7.Fhir.Model.TerminologyCapabilities) },
            { typeof(TerminologyCapabilities_Software), typeof(Hl7.Fhir.Model.TerminologyCapabilities.SoftwareComponent) },
            { typeof(TerminologyCapabilities_Implementation), typeof(Hl7.Fhir.Model.TerminologyCapabilities.ImplementationComponent) },
            { typeof(TerminologyCapabilities_CodeSystem), typeof(Hl7.Fhir.Model.TerminologyCapabilities.CodeSystemComponent) },
            { typeof(TerminologyCapabilities_CodeSystem_Version), typeof(Hl7.Fhir.Model.TerminologyCapabilities.VersionComponent) },
            { typeof(TerminologyCapabilities_CodeSystem_Version_Filter), typeof(Hl7.Fhir.Model.TerminologyCapabilities.FilterComponent) },
            { typeof(TerminologyCapabilities_Expansion), typeof(Hl7.Fhir.Model.TerminologyCapabilities.ExpansionComponent) },
            { typeof(TerminologyCapabilities_Expansion_Parameter), typeof(Hl7.Fhir.Model.TerminologyCapabilities.ParameterComponent) },
            { typeof(TerminologyCapabilities_ValidateCode), typeof(Hl7.Fhir.Model.TerminologyCapabilities.ValidateCodeComponent) },
            { typeof(TerminologyCapabilities_Translation), typeof(Hl7.Fhir.Model.TerminologyCapabilities.TranslationComponent) },
            { typeof(TerminologyCapabilities_Closure), typeof(Hl7.Fhir.Model.TerminologyCapabilities.ClosureComponent) },
            { typeof(TestReport), typeof(Hl7.Fhir.Model.TestReport) },
            { typeof(TestReport_Participant), typeof(Hl7.Fhir.Model.TestReport.ParticipantComponent) },
            { typeof(TestReport_Setup), typeof(Hl7.Fhir.Model.TestReport.SetupComponent) },
            { typeof(TestReport_Setup_Action), typeof(Hl7.Fhir.Model.TestReport.SetupActionComponent) },
            { typeof(TestReport_Setup_Action_Operation), typeof(Hl7.Fhir.Model.TestReport.OperationComponent) },
            { typeof(TestReport_Setup_Action_Assert), typeof(Hl7.Fhir.Model.TestReport.AssertComponent) },
            { typeof(TestReport_Test), typeof(Hl7.Fhir.Model.TestReport.TestComponent) },
            { typeof(TestReport_Test_Action), typeof(Hl7.Fhir.Model.TestReport.TestActionComponent) },
            { typeof(TestReport_Teardown), typeof(Hl7.Fhir.Model.TestReport.TeardownComponent) },
            { typeof(TestReport_Teardown_Action), typeof(Hl7.Fhir.Model.TestReport.TeardownActionComponent) },
            { typeof(TestScript), typeof(Hl7.Fhir.Model.TestScript) },
            { typeof(TestScript_Origin), typeof(Hl7.Fhir.Model.TestScript.OriginComponent) },
            { typeof(TestScript_Destination), typeof(Hl7.Fhir.Model.TestScript.DestinationComponent) },
            { typeof(TestScript_Metadata), typeof(Hl7.Fhir.Model.TestScript.MetadataComponent) },
            { typeof(TestScript_Metadata_Link), typeof(Hl7.Fhir.Model.TestScript.LinkComponent) },
            { typeof(TestScript_Metadata_Capability), typeof(Hl7.Fhir.Model.TestScript.CapabilityComponent) },
            { typeof(TestScript_Fixture), typeof(Hl7.Fhir.Model.TestScript.FixtureComponent) },
            { typeof(TestScript_Variable), typeof(Hl7.Fhir.Model.TestScript.VariableComponent) },
            { typeof(TestScript_Setup), typeof(Hl7.Fhir.Model.TestScript.SetupComponent) },
            { typeof(TestScript_Setup_Action), typeof(Hl7.Fhir.Model.TestScript.SetupActionComponent) },
            { typeof(TestScript_Setup_Action_Operation), typeof(Hl7.Fhir.Model.TestScript.OperationComponent) },
            { typeof(TestScript_Setup_Action_Operation_RequestHeader), typeof(Hl7.Fhir.Model.TestScript.RequestHeaderComponent) },
            { typeof(TestScript_Setup_Action_Assert), typeof(Hl7.Fhir.Model.TestScript.AssertComponent) },
            { typeof(TestScript_Test), typeof(Hl7.Fhir.Model.TestScript.TestComponent) },
            { typeof(TestScript_Test_Action), typeof(Hl7.Fhir.Model.TestScript.TestActionComponent) },
            { typeof(TestScript_Teardown), typeof(Hl7.Fhir.Model.TestScript.TeardownComponent) },
            { typeof(TestScript_Teardown_Action), typeof(Hl7.Fhir.Model.TestScript.TeardownActionComponent) },
            { typeof(ValueSet), typeof(Hl7.Fhir.Model.ValueSet) },
            { typeof(ValueSet_Compose), typeof(Hl7.Fhir.Model.ValueSet.ComposeComponent) },
            { typeof(ValueSet_Compose_Include), typeof(Hl7.Fhir.Model.ValueSet.ConceptSetComponent) },
            { typeof(ValueSet_Compose_Include_Concept), typeof(Hl7.Fhir.Model.ValueSet.ConceptReferenceComponent) },
            { typeof(ValueSet_Compose_Include_Concept_Designation), typeof(Hl7.Fhir.Model.ValueSet.DesignationComponent) },
            { typeof(ValueSet_Compose_Include_Filter), typeof(Hl7.Fhir.Model.ValueSet.FilterComponent) },
            { typeof(ValueSet_Expansion), typeof(Hl7.Fhir.Model.ValueSet.ExpansionComponent) },
            { typeof(ValueSet_Expansion_Parameter), typeof(Hl7.Fhir.Model.ValueSet.ParameterComponent) },
            { typeof(ValueSet_Expansion_Contains), typeof(Hl7.Fhir.Model.ValueSet.ContainsComponent) },
            { typeof(VerificationResult), typeof(Hl7.Fhir.Model.VerificationResult) },
            { typeof(VerificationResult_PrimarySource), typeof(Hl7.Fhir.Model.VerificationResult.PrimarySourceComponent) },
            { typeof(VerificationResult_Attestation), typeof(Hl7.Fhir.Model.VerificationResult.AttestationComponent) },
            { typeof(VerificationResult_Validator), typeof(Hl7.Fhir.Model.VerificationResult.ValidatorComponent) },
            { typeof(VisionPrescription), typeof(Hl7.Fhir.Model.VisionPrescription) },
            { typeof(VisionPrescription_LensSpecification), typeof(Hl7.Fhir.Model.VisionPrescription.LensSpecificationComponent) },
            { typeof(VisionPrescription_LensSpecification_Prism), typeof(Hl7.Fhir.Model.VisionPrescription.PrismComponent) },
            // NOTE(agw): doesn't seem to be used
            //{ typeof(MetadataResource), typeof(Hl7.Fhir.Model.MetadataResource) },
            // NOTE(agw): abstract
            //{ typeof(Element), typeof(Hl7.Fhir.Model.Element) },
            //{ typeof(BackboneElement), typeof(Hl7.Fhir.Model.BackboneElement) },
            { typeof(Address), typeof(Hl7.Fhir.Model.Address) },
            { typeof(Age), typeof(Hl7.Fhir.Model.Age) },
            { typeof(Annotation), typeof(Hl7.Fhir.Model.Annotation) },
            { typeof(Attachment), typeof(Hl7.Fhir.Model.Attachment) },
            { typeof(CodeableConcept), typeof(Hl7.Fhir.Model.CodeableConcept) },
            { typeof(Coding), typeof(Hl7.Fhir.Model.Coding) },
            { typeof(ContactDetail), typeof(Hl7.Fhir.Model.ContactDetail) },
            { typeof(ContactPoint), typeof(Hl7.Fhir.Model.ContactPoint) },
            { typeof(Contributor), typeof(Hl7.Fhir.Model.Contributor) },
            { typeof(Count), typeof(Hl7.Fhir.Model.Count) },
            { typeof(DataRequirement), typeof(Hl7.Fhir.Model.DataRequirement) },
            { typeof(DataRequirement_CodeFilter), typeof(Hl7.Fhir.Model.DataRequirement.CodeFilterComponent) },
            { typeof(DataRequirement_DateFilter), typeof(Hl7.Fhir.Model.DataRequirement.DateFilterComponent) },
            { typeof(DataRequirement_Sort), typeof(Hl7.Fhir.Model.DataRequirement.SortComponent) },
            { typeof(Distance), typeof(Hl7.Fhir.Model.Distance) },
            { typeof(Dosage), typeof(Hl7.Fhir.Model.Dosage) },
            { typeof(Dosage_DoseAndRate), typeof(Hl7.Fhir.Model.Dosage.DoseAndRateComponent) },
            { typeof(Duration), typeof(Hl7.Fhir.Model.Duration) },
            { typeof(ElementDefinition), typeof(Hl7.Fhir.Model.ElementDefinition) },
            { typeof(ElementDefinition_Slicing), typeof(Hl7.Fhir.Model.ElementDefinition.SlicingComponent) },
            { typeof(ElementDefinition_Slicing_Discriminator), typeof(Hl7.Fhir.Model.ElementDefinition.DiscriminatorComponent) },
            { typeof(ElementDefinition_Base), typeof(Hl7.Fhir.Model.ElementDefinition.BaseComponent) },
            { typeof(ElementDefinition_Type), typeof(Hl7.Fhir.Model.ElementDefinition.TypeRefComponent) },
            { typeof(ElementDefinition_Example), typeof(Hl7.Fhir.Model.ElementDefinition.ExampleComponent) },
            { typeof(ElementDefinition_Constraint), typeof(Hl7.Fhir.Model.ElementDefinition.ConstraintComponent) },
            { typeof(ElementDefinition_Binding), typeof(Hl7.Fhir.Model.ElementDefinition.ElementDefinitionBindingComponent) },
            { typeof(ElementDefinition_Mapping), typeof(Hl7.Fhir.Model.ElementDefinition.MappingComponent) },
            { typeof(Expression), typeof(Hl7.Fhir.Model.Expression) },
            { typeof(Extension), typeof(Hl7.Fhir.Model.Extension) },
            { typeof(HumanName), typeof(Hl7.Fhir.Model.HumanName) },
            { typeof(Identifier), typeof(Hl7.Fhir.Model.Identifier) },
            { typeof(MarketingStatus), typeof(Hl7.Fhir.Model.MarketingStatus) },
            { typeof(Meta), typeof(Hl7.Fhir.Model.Meta) },
            { typeof(Money), typeof(Hl7.Fhir.Model.Money) },
            { typeof(Narrative), typeof(Hl7.Fhir.Model.Narrative) },
            { typeof(ParameterDefinition), typeof(Hl7.Fhir.Model.ParameterDefinition) },
            { typeof(Period), typeof(Hl7.Fhir.Model.Period) },
            { typeof(Population), typeof(Hl7.Fhir.Model.Population) },
            { typeof(ProdCharacteristic), typeof(Hl7.Fhir.Model.ProdCharacteristic) },
            { typeof(ProductShelfLife), typeof(Hl7.Fhir.Model.ProductShelfLife) },
            { typeof(Quantity), typeof(Hl7.Fhir.Model.Quantity) },
            { typeof(Range), typeof(Hl7.Fhir.Model.Range) },
            { typeof(Ratio), typeof(Hl7.Fhir.Model.Ratio) },
            { typeof(Reference), typeof(Hl7.Fhir.Model.ResourceReference) },
            { typeof(RelatedArtifact), typeof(Hl7.Fhir.Model.RelatedArtifact) },
            { typeof(SampledData), typeof(Hl7.Fhir.Model.SampledData) },
            { typeof(Signature), typeof(Hl7.Fhir.Model.Signature) },
            { typeof(SubstanceAmount), typeof(Hl7.Fhir.Model.SubstanceAmount) },
            { typeof(SubstanceAmount_ReferenceRange), typeof(Hl7.Fhir.Model.SubstanceAmount.ReferenceRangeComponent) },
            { typeof(Timing), typeof(Hl7.Fhir.Model.Timing) },
            { typeof(Timing_Repeat), typeof(Hl7.Fhir.Model.Timing.RepeatComponent) },
            { typeof(TriggerDefinition), typeof(Hl7.Fhir.Model.TriggerDefinition) },
            { typeof(UsageContext), typeof(Hl7.Fhir.Model.UsageContext) },
            // NOTE(agw): could not find anything "moneyquantity" in firely sdk
            //{ typeof(MoneyQuantity), typeof(Hl7.Fhir.Model.Money) },
            { typeof(SimpleQuantity), typeof(Hl7.Fhir.Model.Quantity) },
            // NOTE(agw): not used by others
            //{ typeof(Shareablemeasure), typeof(Hl7.Fhir.Model.Measure) },
            //{ typeof(Servicerequest_genetics), typeof(Hl7.Fhir.Model.Servicerequest.geneticsComponent) },
            //{ typeof(Groupdefinition), typeof(Hl7.Fhir.Model.GraphDefinition) },
            /*
            { typeof(Actualgroup), typeof(Hl7.Fhir.Model.Actualgroup) },
            { typeof(Familymemberhistory_genetic), typeof(Hl7.Fhir.Model.Familymemberhistory.geneticComponent) },
            { typeof(Shareableactivitydefinition), typeof(Hl7.Fhir.Model.Shareableactivitydefinition) },
            { typeof(Cdshooksrequestgroup), typeof(Hl7.Fhir.Model.Cdshooksrequestgroup) },
            { typeof(Provenance_relevant_history), typeof(Hl7.Fhir.Model.Provenance.relevant.historyComponent) },
            { typeof(Cqf_questionnaire), typeof(Hl7.Fhir.Model.Cqf.questionnaireComponent) },
            { typeof(Shareablevalueset), typeof(Hl7.Fhir.Model.Shareablevalueset) },
            { typeof(Picoelement), typeof(Hl7.Fhir.Model.Picoelement) },
            { typeof(Shareablecodesystem), typeof(Hl7.Fhir.Model.Shareablecodesystem) },
            { typeof(Cdshooksguidanceresponse), typeof(Hl7.Fhir.Model.Cdshooksguidanceresponse) },
            { typeof(Devicemetricobservation), typeof(Hl7.Fhir.Model.Devicemetricobservation) },
            { typeof(Observation_genetics), typeof(Hl7.Fhir.Model.Observation.geneticsComponent) },
            { typeof(Vitalsigns), typeof(Hl7.Fhir.Model.VitalSigns) },
            { typeof(Bodyweight), typeof(Hl7.Fhir.Model.Bodyweight) },
            { typeof(Vitalspanel), typeof(Hl7.Fhir.Model.Vitalspanel) },
            { typeof(Bodyheight), typeof(Hl7.Fhir.Model.Bodyheight) },
            { typeof(Resprate), typeof(Hl7.Fhir.Model.Resprate) },
            { typeof(Heartrate), typeof(Hl7.Fhir.Model.Heartrate) },
            { typeof(Bodytemp), typeof(Hl7.Fhir.Model.Bodytemp) },
            { typeof(Headcircum), typeof(Hl7.Fhir.Model.Headcircum) },
            { typeof(Oxygensat), typeof(Hl7.Fhir.Model.Oxygensat) },
            { typeof(Bmi), typeof(Hl7.Fhir.Model.Bmi) },
            { typeof(Bp), typeof(Hl7.Fhir.Model.Bp) },
            { typeof(Shareablelibrary), typeof(Hl7.Fhir.Model.Shareablelibrary) },
            { typeof(Cqllibrary), typeof(Hl7.Fhir.Model.Cqllibrary) },
            { typeof(Lipidprofile), typeof(Hl7.Fhir.Model.Lipidprofile) },
            { typeof(Cholesterol), typeof(Hl7.Fhir.Model.Cholesterol) },
            { typeof(Triglyceride), typeof(Hl7.Fhir.Model.Triglyceride) },
            { typeof(Hdlcholesterol), typeof(Hl7.Fhir.Model.Hdlcholesterol) },
            { typeof(Ldlcholesterol), typeof(Hl7.Fhir.Model.Ldlcholesterol) },
            { typeof(Diagnosticreport_genetics), typeof(Hl7.Fhir.Model.Diagnosticreport.geneticsComponent) },
            { typeof(Hlaresult), typeof(Hl7.Fhir.Model.Hlaresult) },
            { typeof(Synthesis), typeof(Hl7.Fhir.Model.Synthesis) },
            { typeof(Clinicaldocument), typeof(Hl7.Fhir.Model.Clinicaldocument) },
            { typeof(Catalog), typeof(Hl7.Fhir.Model.Catalog) },
            { typeof(Shareableplandefinition), typeof(Hl7.Fhir.Model.Shareableplandefinition) },
            { typeof(Computableplandefinition), typeof(Hl7.Fhir.Model.Computableplandefinition) },
            { typeof(Cdshooksserviceplandefinition), typeof(Hl7.Fhir.Model.Cdshooksserviceplandefinition) },
            { typeof(Elementdefinition_de), typeof(Hl7.Fhir.Model.Elementdefinition.deComponent) },
            { typeof(Ehrsrle_auditevent), typeof(Hl7.Fhir.Model.Ehrsrle.auditeventComponent) },
            { typeof(Ehrsrle_provenance), typeof(Hl7.Fhir.Model.Ehrsrle.provenanceComponent) },
            */
        };
    }

    public class IndentedStringBuilder
    {
        public int IndentedAmount = 0;
        StringBuilder builder;
        public IndentedStringBuilder()
        {
            builder = new StringBuilder();
        }

        public override string ToString()
        {
            return builder.ToString();
        }

        public void AppendLine(string line)
        {
            for(int i = 0; i < IndentedAmount; i++)
            {
                builder.Append("\t");
            }
            builder.Append(line);
            builder.Append(Environment.NewLine);
        }
    }
}
