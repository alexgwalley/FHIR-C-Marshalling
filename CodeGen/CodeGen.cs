/*
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
using FHIR_Marshalling;

namespace CodeGen
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
            builder.AppendLine($"public static {firelyResourceName}? Marshal_Resource({typeof(FHIR_Marshalling.Resource).FullName}* resource)");
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
            { typeof(FHIR_Marshalling.Resource), typeof(Hl7.Fhir.Model.Resource) },
            { typeof(FHIR_Marshalling.Account), typeof(Hl7.Fhir.Model.Account) },
            { typeof(FHIR_Marshalling.Account_Coverage), typeof(Hl7.Fhir.Model.Account.CoverageComponent) },
            { typeof(FHIR_Marshalling.Account_Guarantor), typeof(Hl7.Fhir.Model.Account.GuarantorComponent) },
            { typeof(FHIR_Marshalling.ActivityDefinition), typeof(Hl7.Fhir.Model.ActivityDefinition) },
            { typeof(FHIR_Marshalling.ActivityDefinition_Participant), typeof(Hl7.Fhir.Model.ActivityDefinition.ParticipantComponent) },
            { typeof(FHIR_Marshalling.ActivityDefinition_DynamicValue), typeof(Hl7.Fhir.Model.ActivityDefinition.DynamicValueComponent) },
            { typeof(FHIR_Marshalling.AdverseEvent), typeof(Hl7.Fhir.Model.AdverseEvent) },
            { typeof(FHIR_Marshalling.AdverseEvent_SuspectEntity), typeof(Hl7.Fhir.Model.AdverseEvent.SuspectEntityComponent) },
            { typeof(FHIR_Marshalling.AdverseEvent_SuspectEntity_Causality), typeof(Hl7.Fhir.Model.AdverseEvent.CausalityComponent) },
            { typeof(FHIR_Marshalling.AllergyIntolerance), typeof(Hl7.Fhir.Model.AllergyIntolerance) },
            { typeof(FHIR_Marshalling.AllergyIntolerance_Reaction), typeof(Hl7.Fhir.Model.AllergyIntolerance.ReactionComponent) },
            { typeof(FHIR_Marshalling.Appointment), typeof(Hl7.Fhir.Model.Appointment) },
            { typeof(FHIR_Marshalling.Appointment_Participant), typeof(Hl7.Fhir.Model.Appointment.ParticipantComponent) },
            { typeof(FHIR_Marshalling.AppointmentResponse), typeof(Hl7.Fhir.Model.AppointmentResponse) },
            { typeof(FHIR_Marshalling.AuditEvent), typeof(Hl7.Fhir.Model.AuditEvent) },
            { typeof(FHIR_Marshalling.AuditEvent_Agent), typeof(Hl7.Fhir.Model.AuditEvent.AgentComponent) },
            { typeof(FHIR_Marshalling.AuditEvent_Agent_Network), typeof(Hl7.Fhir.Model.AuditEvent.NetworkComponent) },
            { typeof(FHIR_Marshalling.AuditEvent_Source), typeof(Hl7.Fhir.Model.AuditEvent.SourceComponent) },
            { typeof(FHIR_Marshalling.AuditEvent_Entity), typeof(Hl7.Fhir.Model.AuditEvent.EntityComponent) },
            { typeof(FHIR_Marshalling.AuditEvent_Entity_Detail), typeof(Hl7.Fhir.Model.AuditEvent.DetailComponent) },
            { typeof(FHIR_Marshalling.Basic), typeof(Hl7.Fhir.Model.Basic) },
            { typeof(FHIR_Marshalling.Binary), typeof(Hl7.Fhir.Model.Binary) },
            { typeof(FHIR_Marshalling.BiologicallyDerivedProduct), typeof(Hl7.Fhir.Model.BiologicallyDerivedProduct) },
            { typeof(FHIR_Marshalling.BiologicallyDerivedProduct_Collection), typeof(Hl7.Fhir.Model.BiologicallyDerivedProduct.CollectionComponent) },
            { typeof(FHIR_Marshalling.BiologicallyDerivedProduct_Processing), typeof(Hl7.Fhir.Model.BiologicallyDerivedProduct.ProcessingComponent) },
            { typeof(FHIR_Marshalling.BiologicallyDerivedProduct_Manipulation), typeof(Hl7.Fhir.Model.BiologicallyDerivedProduct.ManipulationComponent) },
            { typeof(FHIR_Marshalling.BiologicallyDerivedProduct_Storage), typeof(Hl7.Fhir.Model.BiologicallyDerivedProduct.StorageComponent) },
            { typeof(FHIR_Marshalling.BodyStructure), typeof(Hl7.Fhir.Model.BodyStructure) },
            { typeof(FHIR_Marshalling.Bundle), typeof(Hl7.Fhir.Model.Bundle) },
            { typeof(FHIR_Marshalling.Bundle_Link), typeof(Hl7.Fhir.Model.Bundle.LinkComponent) },
            { typeof(FHIR_Marshalling.Bundle_Entry), typeof(Hl7.Fhir.Model.Bundle.EntryComponent) },
            { typeof(FHIR_Marshalling.Bundle_Entry_Search), typeof(Hl7.Fhir.Model.Bundle.SearchComponent) },
            { typeof(FHIR_Marshalling.Bundle_Entry_Request), typeof(Hl7.Fhir.Model.Bundle.RequestComponent) },
            { typeof(FHIR_Marshalling.Bundle_Entry_Response), typeof(Hl7.Fhir.Model.Bundle.ResponseComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement), typeof(Hl7.Fhir.Model.CapabilityStatement) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Software), typeof(Hl7.Fhir.Model.CapabilityStatement.SoftwareComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Implementation), typeof(Hl7.Fhir.Model.CapabilityStatement.ImplementationComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Rest), typeof(Hl7.Fhir.Model.CapabilityStatement.RestComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Rest_Security), typeof(Hl7.Fhir.Model.CapabilityStatement.SecurityComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Rest_Resource), typeof(Hl7.Fhir.Model.CapabilityStatement.ResourceComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Rest_Resource_Interaction), typeof(Hl7.Fhir.Model.CapabilityStatement.ResourceInteractionComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Rest_Resource_SearchParam), typeof(Hl7.Fhir.Model.CapabilityStatement.SearchParamComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Rest_Resource_Operation), typeof(Hl7.Fhir.Model.CapabilityStatement.OperationComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Rest_Interaction), typeof(Hl7.Fhir.Model.CapabilityStatement.SystemInteractionComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Messaging), typeof(Hl7.Fhir.Model.CapabilityStatement.MessagingComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Messaging_Endpoint), typeof(Hl7.Fhir.Model.CapabilityStatement.EndpointComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Messaging_SupportedMessage), typeof(Hl7.Fhir.Model.CapabilityStatement.SupportedMessageComponent) },
            { typeof(FHIR_Marshalling.CapabilityStatement_Document), typeof(Hl7.Fhir.Model.CapabilityStatement.DocumentComponent) },
            { typeof(FHIR_Marshalling.CarePlan), typeof(Hl7.Fhir.Model.CarePlan) },
            { typeof(FHIR_Marshalling.CarePlan_Activity), typeof(Hl7.Fhir.Model.CarePlan.ActivityComponent) },
            { typeof(FHIR_Marshalling.CarePlan_Activity_Detail), typeof(Hl7.Fhir.Model.CarePlan.DetailComponent) },
            { typeof(FHIR_Marshalling.CareTeam), typeof(Hl7.Fhir.Model.CareTeam) },
            { typeof(FHIR_Marshalling.CareTeam_Participant), typeof(Hl7.Fhir.Model.CareTeam.ParticipantComponent) },
            { typeof(FHIR_Marshalling.CatalogEntry), typeof(Hl7.Fhir.Model.CatalogEntry) },
            { typeof(FHIR_Marshalling.CatalogEntry_RelatedEntry), typeof(Hl7.Fhir.Model.CatalogEntry.RelatedEntryComponent) },
            { typeof(FHIR_Marshalling.ChargeItem), typeof(Hl7.Fhir.Model.ChargeItem) },
            { typeof(FHIR_Marshalling.ChargeItem_Performer), typeof(Hl7.Fhir.Model.ChargeItem.PerformerComponent) },
            { typeof(FHIR_Marshalling.ChargeItemDefinition), typeof(Hl7.Fhir.Model.ChargeItemDefinition) },
            { typeof(FHIR_Marshalling.ChargeItemDefinition_Applicability), typeof(Hl7.Fhir.Model.ChargeItemDefinition.ApplicabilityComponent) },
            { typeof(FHIR_Marshalling.ChargeItemDefinition_PropertyGroup), typeof(Hl7.Fhir.Model.ChargeItemDefinition.PropertyGroupComponent) },
            { typeof(FHIR_Marshalling.ChargeItemDefinition_PropertyGroup_PriceComponent), typeof(Hl7.Fhir.Model.ChargeItemDefinition.PriceComponentComponent) },
            { typeof(FHIR_Marshalling.Claim), typeof(Hl7.Fhir.Model.Claim) },
            { typeof(FHIR_Marshalling.Claim_Related), typeof(Hl7.Fhir.Model.Claim.RelatedClaimComponent) },
            { typeof(FHIR_Marshalling.Claim_Payee), typeof(Hl7.Fhir.Model.Claim.PayeeComponent) },
            { typeof(FHIR_Marshalling.Claim_CareTeam), typeof(Hl7.Fhir.Model.Claim.CareTeamComponent) },
            { typeof(FHIR_Marshalling.Claim_SupportingInfo), typeof(Hl7.Fhir.Model.Claim.SupportingInformationComponent) },
            { typeof(FHIR_Marshalling.Claim_Diagnosis), typeof(Hl7.Fhir.Model.Claim.DiagnosisComponent) },
            { typeof(FHIR_Marshalling.Claim_Procedure), typeof(Hl7.Fhir.Model.Claim.ProcedureComponent) },
            { typeof(FHIR_Marshalling.Claim_Insurance), typeof(Hl7.Fhir.Model.Claim.InsuranceComponent) },
            { typeof(FHIR_Marshalling.Claim_Accident), typeof(Hl7.Fhir.Model.Claim.AccidentComponent) },
            { typeof(FHIR_Marshalling.Claim_Item), typeof(Hl7.Fhir.Model.Claim.ItemComponent) },
            { typeof(FHIR_Marshalling.Claim_Item_Detail), typeof(Hl7.Fhir.Model.Claim.DetailComponent) },
            { typeof(FHIR_Marshalling.Claim_Item_Detail_SubDetail), typeof(Hl7.Fhir.Model.Claim.SubDetailComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse), typeof(Hl7.Fhir.Model.ClaimResponse) },
            { typeof(FHIR_Marshalling.ClaimResponse_Item), typeof(Hl7.Fhir.Model.ClaimResponse.ItemComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse_Item_Adjudication), typeof(Hl7.Fhir.Model.ClaimResponse.AdjudicationComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse_Item_Detail), typeof(Hl7.Fhir.Model.ClaimResponse.ItemDetailComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse_Item_Detail_SubDetail), typeof(Hl7.Fhir.Model.ClaimResponse.SubDetailComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse_AddItem), typeof(Hl7.Fhir.Model.ClaimResponse.AddedItemComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse_AddItem_Detail), typeof(Hl7.Fhir.Model.ClaimResponse.AddedItemDetailComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse_AddItem_Detail_SubDetail), typeof(Hl7.Fhir.Model.ClaimResponse.AddedItemSubDetailComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse_Total), typeof(Hl7.Fhir.Model.ClaimResponse.TotalComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse_Payment), typeof(Hl7.Fhir.Model.ClaimResponse.PaymentComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse_ProcessNote), typeof(Hl7.Fhir.Model.ClaimResponse.NoteComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse_Insurance), typeof(Hl7.Fhir.Model.ClaimResponse.InsuranceComponent) },
            { typeof(FHIR_Marshalling.ClaimResponse_Error), typeof(Hl7.Fhir.Model.ClaimResponse.ErrorComponent) },
            { typeof(FHIR_Marshalling.ClinicalImpression), typeof(Hl7.Fhir.Model.ClinicalImpression) },
            { typeof(FHIR_Marshalling.ClinicalImpression_Investigation), typeof(Hl7.Fhir.Model.ClinicalImpression.InvestigationComponent) },
            { typeof(FHIR_Marshalling.ClinicalImpression_Finding), typeof(Hl7.Fhir.Model.ClinicalImpression.FindingComponent) },
            { typeof(FHIR_Marshalling.CodeSystem), typeof(Hl7.Fhir.Model.CodeSystem) },
            { typeof(FHIR_Marshalling.CodeSystem_Filter), typeof(Hl7.Fhir.Model.CodeSystem.FilterComponent) },
            { typeof(FHIR_Marshalling.CodeSystem_Property), typeof(Hl7.Fhir.Model.CodeSystem.PropertyComponent) },
            { typeof(FHIR_Marshalling.CodeSystem_Concept), typeof(Hl7.Fhir.Model.CodeSystem.ConceptDefinitionComponent) },
            { typeof(FHIR_Marshalling.CodeSystem_Concept_Designation), typeof(Hl7.Fhir.Model.CodeSystem.DesignationComponent) },
            { typeof(FHIR_Marshalling.CodeSystem_Concept_Property), typeof(Hl7.Fhir.Model.CodeSystem.ConceptPropertyComponent) },
            { typeof(FHIR_Marshalling.Communication), typeof(Hl7.Fhir.Model.Communication) },
            { typeof(FHIR_Marshalling.Communication_Payload), typeof(Hl7.Fhir.Model.Communication.PayloadComponent) },
            { typeof(FHIR_Marshalling.CommunicationRequest), typeof(Hl7.Fhir.Model.CommunicationRequest) },
            { typeof(FHIR_Marshalling.CommunicationRequest_Payload), typeof(Hl7.Fhir.Model.CommunicationRequest.PayloadComponent) },
            { typeof(FHIR_Marshalling.CompartmentDefinition), typeof(Hl7.Fhir.Model.CompartmentDefinition) },
            { typeof(FHIR_Marshalling.CompartmentDefinition_Resource), typeof(Hl7.Fhir.Model.CompartmentDefinition.ResourceComponent) },
            { typeof(FHIR_Marshalling.Composition), typeof(Hl7.Fhir.Model.Composition) },
            { typeof(FHIR_Marshalling.Composition_Attester), typeof(Hl7.Fhir.Model.Composition.AttesterComponent) },
            { typeof(FHIR_Marshalling.Composition_RelatesTo), typeof(Hl7.Fhir.Model.Composition.RelatesToComponent) },
            { typeof(FHIR_Marshalling.Composition_Event), typeof(Hl7.Fhir.Model.Composition.EventComponent) },
            { typeof(FHIR_Marshalling.Composition_Section), typeof(Hl7.Fhir.Model.Composition.SectionComponent) },
            { typeof(FHIR_Marshalling.ConceptMap), typeof(Hl7.Fhir.Model.ConceptMap) },
            { typeof(FHIR_Marshalling.ConceptMap_Group), typeof(Hl7.Fhir.Model.ConceptMap.GroupComponent) },
            { typeof(FHIR_Marshalling.ConceptMap_Group_Element), typeof(Hl7.Fhir.Model.ConceptMap.SourceElementComponent) },
            { typeof(FHIR_Marshalling.ConceptMap_Group_Element_Target), typeof(Hl7.Fhir.Model.ConceptMap.TargetElementComponent) },
            { typeof(FHIR_Marshalling.ConceptMap_Group_Element_Target_DependsOn), typeof(Hl7.Fhir.Model.ConceptMap.OtherElementComponent) },
            { typeof(FHIR_Marshalling.ConceptMap_Group_Unmapped), typeof(Hl7.Fhir.Model.ConceptMap.UnmappedComponent) },
            { typeof(FHIR_Marshalling.Condition), typeof(Hl7.Fhir.Model.Condition) },
            { typeof(FHIR_Marshalling.Condition_Stage), typeof(Hl7.Fhir.Model.Condition.StageComponent) },
            { typeof(FHIR_Marshalling.Condition_Evidence), typeof(Hl7.Fhir.Model.Condition.EvidenceComponent) },
            { typeof(FHIR_Marshalling.Consent), typeof(Hl7.Fhir.Model.Consent) },
            { typeof(FHIR_Marshalling.Consent_Policy), typeof(Hl7.Fhir.Model.Consent.PolicyComponent) },
            { typeof(FHIR_Marshalling.Consent_Verification), typeof(Hl7.Fhir.Model.Consent.VerificationComponent) },
            { typeof(FHIR_Marshalling.Consent_Provision), typeof(Hl7.Fhir.Model.Consent.provisionComponent) },
            { typeof(FHIR_Marshalling.Consent_Provision_Actor), typeof(Hl7.Fhir.Model.Consent.provisionActorComponent) },
            { typeof(FHIR_Marshalling.Consent_Provision_Data), typeof(Hl7.Fhir.Model.Consent.provisionDataComponent) },
            { typeof(FHIR_Marshalling.Contract), typeof(Hl7.Fhir.Model.Contract) },
            { typeof(FHIR_Marshalling.Contract_ContentDefinition), typeof(Hl7.Fhir.Model.Contract.ContentDefinitionComponent) },
            { typeof(FHIR_Marshalling.Contract_Term), typeof(Hl7.Fhir.Model.Contract.TermComponent) },
            { typeof(FHIR_Marshalling.Contract_Term_SecurityLabel), typeof(Hl7.Fhir.Model.Contract.SecurityLabelComponent) },
            { typeof(FHIR_Marshalling.Contract_Term_Offer), typeof(Hl7.Fhir.Model.Contract.ContractOfferComponent) },
            { typeof(FHIR_Marshalling.Contract_Term_Offer_Party), typeof(Hl7.Fhir.Model.Contract.ContractPartyComponent) },
            { typeof(FHIR_Marshalling.Contract_Term_Offer_Answer), typeof(Hl7.Fhir.Model.Contract.AnswerComponent) },
            { typeof(FHIR_Marshalling.Contract_Term_Asset), typeof(Hl7.Fhir.Model.Contract.ContractAssetComponent) },
            { typeof(FHIR_Marshalling.Contract_Term_Asset_Context), typeof(Hl7.Fhir.Model.Contract.AssetContextComponent) },
            { typeof(FHIR_Marshalling.Contract_Term_Asset_ValuedItem), typeof(Hl7.Fhir.Model.Contract.ValuedItemComponent) },
            { typeof(FHIR_Marshalling.Contract_Term_Action), typeof(Hl7.Fhir.Model.Contract.ActionComponent) },
            { typeof(FHIR_Marshalling.Contract_Term_Action_Subject), typeof(Hl7.Fhir.Model.Contract.ActionSubjectComponent) },
            { typeof(FHIR_Marshalling.Contract_Signer), typeof(Hl7.Fhir.Model.Contract.SignatoryComponent) },
            { typeof(FHIR_Marshalling.Contract_Friendly), typeof(Hl7.Fhir.Model.Contract.FriendlyLanguageComponent) },
            { typeof(FHIR_Marshalling.Contract_Legal), typeof(Hl7.Fhir.Model.Contract.LegalLanguageComponent) },
            { typeof(FHIR_Marshalling.Contract_Rule), typeof(Hl7.Fhir.Model.Contract.ComputableLanguageComponent) },
            { typeof(FHIR_Marshalling.Coverage), typeof(Hl7.Fhir.Model.Coverage) },
            { typeof(FHIR_Marshalling.Coverage_Class), typeof(Hl7.Fhir.Model.Coverage.ClassComponent) },
            { typeof(FHIR_Marshalling.Coverage_CostToBeneficiary), typeof(Hl7.Fhir.Model.Coverage.CostToBeneficiaryComponent) },
            { typeof(FHIR_Marshalling.Coverage_CostToBeneficiary_Exception), typeof(Hl7.Fhir.Model.Coverage.ExemptionComponent) },
            { typeof(FHIR_Marshalling.CoverageEligibilityRequest), typeof(Hl7.Fhir.Model.CoverageEligibilityRequest) },
            { typeof(FHIR_Marshalling.CoverageEligibilityRequest_SupportingInfo), typeof(Hl7.Fhir.Model.CoverageEligibilityRequest.SupportingInformationComponent) },
            { typeof(FHIR_Marshalling.CoverageEligibilityRequest_Insurance), typeof(Hl7.Fhir.Model.CoverageEligibilityRequest.InsuranceComponent) },
            { typeof(FHIR_Marshalling.CoverageEligibilityRequest_Item), typeof(Hl7.Fhir.Model.CoverageEligibilityRequest.DetailsComponent) },
            { typeof(FHIR_Marshalling.CoverageEligibilityRequest_Item_Diagnosis), typeof(Hl7.Fhir.Model.CoverageEligibilityRequest.DiagnosisComponent) },
            { typeof(FHIR_Marshalling.CoverageEligibilityResponse), typeof(Hl7.Fhir.Model.CoverageEligibilityResponse) },
            { typeof(FHIR_Marshalling.CoverageEligibilityResponse_Insurance), typeof(Hl7.Fhir.Model.CoverageEligibilityResponse.InsuranceComponent) },
            { typeof(FHIR_Marshalling.CoverageEligibilityResponse_Insurance_Item), typeof(Hl7.Fhir.Model.CoverageEligibilityResponse.ItemsComponent) },
            { typeof(FHIR_Marshalling.CoverageEligibilityResponse_Insurance_Item_Benefit), typeof(Hl7.Fhir.Model.CoverageEligibilityResponse.BenefitComponent) },
            { typeof(FHIR_Marshalling.CoverageEligibilityResponse_Error), typeof(Hl7.Fhir.Model.CoverageEligibilityResponse.ErrorsComponent) },
            { typeof(FHIR_Marshalling.DetectedIssue), typeof(Hl7.Fhir.Model.DetectedIssue) },
            { typeof(FHIR_Marshalling.DetectedIssue_Evidence), typeof(Hl7.Fhir.Model.DetectedIssue.EvidenceComponent) },
            { typeof(FHIR_Marshalling.DetectedIssue_Mitigation), typeof(Hl7.Fhir.Model.DetectedIssue.MitigationComponent) },
            { typeof(FHIR_Marshalling.Device), typeof(Hl7.Fhir.Model.Device) },
            { typeof(FHIR_Marshalling.Device_UdiCarrier), typeof(Hl7.Fhir.Model.Device.UdiCarrierComponent) },
            { typeof(FHIR_Marshalling.Device_DeviceName), typeof(Hl7.Fhir.Model.Device.DeviceNameComponent) },
            { typeof(FHIR_Marshalling.Device_Specialization), typeof(Hl7.Fhir.Model.Device.SpecializationComponent) },
            { typeof(FHIR_Marshalling.Device_Version), typeof(Hl7.Fhir.Model.Device.VersionComponent) },
            { typeof(FHIR_Marshalling.Device_Property), typeof(Hl7.Fhir.Model.Device.PropertyComponent) },
            { typeof(FHIR_Marshalling.DeviceDefinition), typeof(Hl7.Fhir.Model.DeviceDefinition) },
            { typeof(FHIR_Marshalling.DeviceDefinition_UdiDeviceIdentifier), typeof(Hl7.Fhir.Model.DeviceDefinition.UdiDeviceIdentifierComponent) },
            { typeof(FHIR_Marshalling.DeviceDefinition_DeviceName), typeof(Hl7.Fhir.Model.DeviceDefinition.DeviceNameComponent) },
            { typeof(FHIR_Marshalling.DeviceDefinition_Specialization), typeof(Hl7.Fhir.Model.DeviceDefinition.SpecializationComponent) },
            { typeof(FHIR_Marshalling.DeviceDefinition_Capability), typeof(Hl7.Fhir.Model.DeviceDefinition.CapabilityComponent) },
            { typeof(FHIR_Marshalling.DeviceDefinition_Property), typeof(Hl7.Fhir.Model.DeviceDefinition.PropertyComponent) },
            { typeof(FHIR_Marshalling.DeviceDefinition_Material), typeof(Hl7.Fhir.Model.DeviceDefinition.MaterialComponent) },
            { typeof(FHIR_Marshalling.DeviceMetric), typeof(Hl7.Fhir.Model.DeviceMetric) },
            { typeof(FHIR_Marshalling.DeviceMetric_Calibration), typeof(Hl7.Fhir.Model.DeviceMetric.CalibrationComponent) },
            { typeof(FHIR_Marshalling.DeviceRequest), typeof(Hl7.Fhir.Model.DeviceRequest) },
            { typeof(FHIR_Marshalling.DeviceRequest_Parameter), typeof(Hl7.Fhir.Model.DeviceRequest.ParameterComponent) },
            { typeof(FHIR_Marshalling.DeviceUseStatement), typeof(Hl7.Fhir.Model.DeviceUseStatement) },
            { typeof(FHIR_Marshalling.DiagnosticReport), typeof(Hl7.Fhir.Model.DiagnosticReport) },
            { typeof(FHIR_Marshalling.DiagnosticReport_Media), typeof(Hl7.Fhir.Model.DiagnosticReport.MediaComponent) },
            { typeof(FHIR_Marshalling.DocumentManifest), typeof(Hl7.Fhir.Model.DocumentManifest) },
            { typeof(FHIR_Marshalling.DocumentManifest_Related), typeof(Hl7.Fhir.Model.DocumentManifest.RelatedComponent) },
            { typeof(FHIR_Marshalling.DocumentReference), typeof(Hl7.Fhir.Model.DocumentReference) },
            { typeof(FHIR_Marshalling.DocumentReference_RelatesTo), typeof(Hl7.Fhir.Model.DocumentReference.RelatesToComponent) },
            { typeof(FHIR_Marshalling.DocumentReference_Content), typeof(Hl7.Fhir.Model.DocumentReference.ContentComponent) },
            { typeof(FHIR_Marshalling.DocumentReference_Context), typeof(Hl7.Fhir.Model.DocumentReference.ContextComponent) },
            // NOTE(agw): abstract, cannot create
            //{ typeof(DomainResource), typeof(Hl7.Fhir.Model.DomainResource) },
            { typeof(FHIR_Marshalling.EffectEvidenceSynthesis), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis) },
            { typeof(FHIR_Marshalling.EffectEvidenceSynthesis_SampleSize), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.SampleSizeComponent) },
            { typeof(FHIR_Marshalling.EffectEvidenceSynthesis_ResultsByExposure), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.ResultsByExposureComponent) },
            { typeof(FHIR_Marshalling.EffectEvidenceSynthesis_EffectEstimate), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.EffectEstimateComponent) },
            { typeof(FHIR_Marshalling.EffectEvidenceSynthesis_EffectEstimate_PrecisionEstimate), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.PrecisionEstimateComponent) },
            { typeof(FHIR_Marshalling.EffectEvidenceSynthesis_Certainty), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.CertaintyComponent) },
            { typeof(FHIR_Marshalling.EffectEvidenceSynthesis_Certainty_CertaintySubcomponent), typeof(Hl7.Fhir.Model.EffectEvidenceSynthesis.CertaintySubcomponentComponent) },
            { typeof(FHIR_Marshalling.Encounter), typeof(Hl7.Fhir.Model.Encounter) },
            { typeof(FHIR_Marshalling.Encounter_StatusHistory), typeof(Hl7.Fhir.Model.Encounter.StatusHistoryComponent) },
            { typeof(FHIR_Marshalling.Encounter_ClassHistory), typeof(Hl7.Fhir.Model.Encounter.ClassHistoryComponent) },
            { typeof(FHIR_Marshalling.Encounter_Participant), typeof(Hl7.Fhir.Model.Encounter.ParticipantComponent) },
            { typeof(FHIR_Marshalling.Encounter_Diagnosis), typeof(Hl7.Fhir.Model.Encounter.DiagnosisComponent) },
            { typeof(FHIR_Marshalling.Encounter_Hospitalization), typeof(Hl7.Fhir.Model.Encounter.HospitalizationComponent) },
            { typeof(FHIR_Marshalling.Encounter_Location), typeof(Hl7.Fhir.Model.Encounter.LocationComponent) },
            { typeof(FHIR_Marshalling.Endpoint), typeof(Hl7.Fhir.Model.Endpoint) },
            { typeof(FHIR_Marshalling.EnrollmentRequest), typeof(Hl7.Fhir.Model.EnrollmentRequest) },
            { typeof(FHIR_Marshalling.EnrollmentResponse), typeof(Hl7.Fhir.Model.EnrollmentResponse) },
            { typeof(FHIR_Marshalling.EpisodeOfCare), typeof(Hl7.Fhir.Model.EpisodeOfCare) },
            { typeof(FHIR_Marshalling.EpisodeOfCare_StatusHistory), typeof(Hl7.Fhir.Model.EpisodeOfCare.StatusHistoryComponent) },
            { typeof(FHIR_Marshalling.EpisodeOfCare_Diagnosis), typeof(Hl7.Fhir.Model.EpisodeOfCare.DiagnosisComponent) },
            { typeof(FHIR_Marshalling.EventDefinition), typeof(Hl7.Fhir.Model.EventDefinition) },
            { typeof(FHIR_Marshalling.Evidence), typeof(Hl7.Fhir.Model.Evidence) },
            { typeof(FHIR_Marshalling.EvidenceVariable), typeof(Hl7.Fhir.Model.EvidenceVariable) },
            { typeof(FHIR_Marshalling.EvidenceVariable_Characteristic), typeof(Hl7.Fhir.Model.EvidenceVariable.CharacteristicComponent) },
            { typeof(FHIR_Marshalling.ExampleScenario), typeof(Hl7.Fhir.Model.ExampleScenario) },
            { typeof(FHIR_Marshalling.ExampleScenario_Actor), typeof(Hl7.Fhir.Model.ExampleScenario.ActorComponent) },
            { typeof(FHIR_Marshalling.ExampleScenario_Instance), typeof(Hl7.Fhir.Model.ExampleScenario.InstanceComponent) },
            { typeof(FHIR_Marshalling.ExampleScenario_Instance_Version), typeof(Hl7.Fhir.Model.ExampleScenario.VersionComponent) },
            { typeof(FHIR_Marshalling.ExampleScenario_Instance_ContainedInstance), typeof(Hl7.Fhir.Model.ExampleScenario.ContainedInstanceComponent) },
            { typeof(FHIR_Marshalling.ExampleScenario_Process), typeof(Hl7.Fhir.Model.ExampleScenario.ProcessComponent) },
            { typeof(FHIR_Marshalling.ExampleScenario_Process_Step), typeof(Hl7.Fhir.Model.ExampleScenario.StepComponent) },
            { typeof(FHIR_Marshalling.ExampleScenario_Process_Step_Operation), typeof(Hl7.Fhir.Model.ExampleScenario.OperationComponent) },
            { typeof(FHIR_Marshalling.ExampleScenario_Process_Step_Alternative), typeof(Hl7.Fhir.Model.ExampleScenario.AlternativeComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit), typeof(Hl7.Fhir.Model.ExplanationOfBenefit) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Related), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.RelatedClaimComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Payee), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.PayeeComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_CareTeam), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.CareTeamComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_SupportingInfo), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.SupportingInformationComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Diagnosis), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.DiagnosisComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Procedure), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.ProcedureComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Insurance), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.InsuranceComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Accident), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.AccidentComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Item), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.ItemComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Item_Adjudication), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.AdjudicationComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Item_Detail), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.DetailComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Item_Detail_SubDetail), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.SubDetailComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_AddItem), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.AddedItemComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_AddItem_Detail), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.AddedItemDetailComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_AddItem_Detail_SubDetail), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.AddedItemDetailSubDetailComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Total), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.TotalComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_Payment), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.PaymentComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_ProcessNote), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.NoteComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_BenefitBalance), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.BenefitBalanceComponent) },
            { typeof(FHIR_Marshalling.ExplanationOfBenefit_BenefitBalance_Financial), typeof(Hl7.Fhir.Model.ExplanationOfBenefit.BenefitComponent) },
            { typeof(FHIR_Marshalling.FamilyMemberHistory), typeof(Hl7.Fhir.Model.FamilyMemberHistory) },
            { typeof(FHIR_Marshalling.FamilyMemberHistory_Condition), typeof(Hl7.Fhir.Model.FamilyMemberHistory.ConditionComponent) },
            { typeof(FHIR_Marshalling.Flag), typeof(Hl7.Fhir.Model.Flag) },
            { typeof(FHIR_Marshalling.Goal), typeof(Hl7.Fhir.Model.Goal) },
            { typeof(FHIR_Marshalling.Goal_Target), typeof(Hl7.Fhir.Model.Goal.TargetComponent) },
            { typeof(FHIR_Marshalling.GraphDefinition), typeof(Hl7.Fhir.Model.GraphDefinition) },
            { typeof(FHIR_Marshalling.GraphDefinition_Link), typeof(Hl7.Fhir.Model.GraphDefinition.LinkComponent) },
            { typeof(FHIR_Marshalling.GraphDefinition_Link_Target), typeof(Hl7.Fhir.Model.GraphDefinition.TargetComponent) },
            { typeof(FHIR_Marshalling.GraphDefinition_Link_Target_Compartment), typeof(Hl7.Fhir.Model.GraphDefinition.CompartmentComponent) },
            { typeof(FHIR_Marshalling.Group), typeof(Hl7.Fhir.Model.Group) },
            { typeof(FHIR_Marshalling.Group_Characteristic), typeof(Hl7.Fhir.Model.Group.CharacteristicComponent) },
            { typeof(FHIR_Marshalling.Group_Member), typeof(Hl7.Fhir.Model.Group.MemberComponent) },
            { typeof(FHIR_Marshalling.GuidanceResponse), typeof(Hl7.Fhir.Model.GuidanceResponse) },
            { typeof(FHIR_Marshalling.HealthcareService), typeof(Hl7.Fhir.Model.HealthcareService) },
            { typeof(FHIR_Marshalling.HealthcareService_Eligibility), typeof(Hl7.Fhir.Model.HealthcareService.EligibilityComponent) },
            { typeof(FHIR_Marshalling.HealthcareService_AvailableTime), typeof(Hl7.Fhir.Model.HealthcareService.AvailableTimeComponent) },
            { typeof(FHIR_Marshalling.HealthcareService_NotAvailable), typeof(Hl7.Fhir.Model.HealthcareService.NotAvailableComponent) },
            { typeof(FHIR_Marshalling.ImagingStudy), typeof(Hl7.Fhir.Model.ImagingStudy) },
            { typeof(FHIR_Marshalling.ImagingStudy_Series), typeof(Hl7.Fhir.Model.ImagingStudy.SeriesComponent) },
            { typeof(FHIR_Marshalling.ImagingStudy_Series_Performer), typeof(Hl7.Fhir.Model.ImagingStudy.PerformerComponent) },
            { typeof(FHIR_Marshalling.ImagingStudy_Series_Instance), typeof(Hl7.Fhir.Model.ImagingStudy.InstanceComponent) },
            { typeof(FHIR_Marshalling.Immunization), typeof(Hl7.Fhir.Model.Immunization) },
            { typeof(FHIR_Marshalling.Immunization_Performer), typeof(Hl7.Fhir.Model.Immunization.PerformerComponent) },
            { typeof(FHIR_Marshalling.Immunization_Education), typeof(Hl7.Fhir.Model.Immunization.EducationComponent) },
            { typeof(FHIR_Marshalling.Immunization_Reaction), typeof(Hl7.Fhir.Model.Immunization.ReactionComponent) },
            { typeof(FHIR_Marshalling.Immunization_ProtocolApplied), typeof(Hl7.Fhir.Model.Immunization.ProtocolAppliedComponent) },
            { typeof(FHIR_Marshalling.ImmunizationEvaluation), typeof(Hl7.Fhir.Model.ImmunizationEvaluation) },
            { typeof(FHIR_Marshalling.ImmunizationRecommendation), typeof(Hl7.Fhir.Model.ImmunizationRecommendation) },
            { typeof(FHIR_Marshalling.ImmunizationRecommendation_Recommendation), typeof(Hl7.Fhir.Model.ImmunizationRecommendation.RecommendationComponent) },
            { typeof(FHIR_Marshalling.ImmunizationRecommendation_Recommendation_DateCriterion), typeof(Hl7.Fhir.Model.ImmunizationRecommendation.DateCriterionComponent) },
            { typeof(FHIR_Marshalling.ImplementationGuide), typeof(Hl7.Fhir.Model.ImplementationGuide) },
            { typeof(FHIR_Marshalling.ImplementationGuide_DependsOn), typeof(Hl7.Fhir.Model.ImplementationGuide.DependsOnComponent) },
            { typeof(FHIR_Marshalling.ImplementationGuide_Global), typeof(Hl7.Fhir.Model.ImplementationGuide.GlobalComponent) },
            { typeof(FHIR_Marshalling.ImplementationGuide_Definition), typeof(Hl7.Fhir.Model.ImplementationGuide.DefinitionComponent) },
            { typeof(FHIR_Marshalling.ImplementationGuide_Definition_Grouping), typeof(Hl7.Fhir.Model.ImplementationGuide.GroupingComponent) },
            { typeof(FHIR_Marshalling.ImplementationGuide_Definition_Resource), typeof(Hl7.Fhir.Model.ImplementationGuide.ResourceComponent) },
            { typeof(FHIR_Marshalling.ImplementationGuide_Definition_Page), typeof(Hl7.Fhir.Model.ImplementationGuide.PageComponent) },
            { typeof(FHIR_Marshalling.ImplementationGuide_Definition_Parameter), typeof(Hl7.Fhir.Model.ImplementationGuide.ParameterComponent) },
            { typeof(FHIR_Marshalling.ImplementationGuide_Definition_Template), typeof(Hl7.Fhir.Model.ImplementationGuide.TemplateComponent) },
            { typeof(FHIR_Marshalling.ImplementationGuide_Manifest), typeof(Hl7.Fhir.Model.ImplementationGuide.ManifestComponent) },
            { typeof(FHIR_Marshalling.ImplementationGuide_Manifest_Resource), typeof(Hl7.Fhir.Model.ImplementationGuide.ManifestResourceComponent) },
            { typeof(FHIR_Marshalling.ImplementationGuide_Manifest_Page), typeof(Hl7.Fhir.Model.ImplementationGuide.ManifestPageComponent) },
            { typeof(FHIR_Marshalling.InsurancePlan), typeof(Hl7.Fhir.Model.InsurancePlan) },
            { typeof(FHIR_Marshalling.InsurancePlan_Contact), typeof(Hl7.Fhir.Model.InsurancePlan.ContactComponent) },
            { typeof(FHIR_Marshalling.InsurancePlan_Coverage), typeof(Hl7.Fhir.Model.InsurancePlan.CoverageComponent) },
            { typeof(FHIR_Marshalling.InsurancePlan_Coverage_Benefit), typeof(Hl7.Fhir.Model.InsurancePlan.CoverageBenefitComponent) },
            { typeof(FHIR_Marshalling.InsurancePlan_Coverage_Benefit_Limit), typeof(Hl7.Fhir.Model.InsurancePlan.LimitComponent) },
            { typeof(FHIR_Marshalling.InsurancePlan_Plan), typeof(Hl7.Fhir.Model.InsurancePlan.PlanComponent) },
            { typeof(FHIR_Marshalling.InsurancePlan_Plan_GeneralCost), typeof(Hl7.Fhir.Model.InsurancePlan.GeneralCostComponent) },
            { typeof(FHIR_Marshalling.InsurancePlan_Plan_SpecificCost), typeof(Hl7.Fhir.Model.InsurancePlan.SpecificCostComponent) },
            { typeof(FHIR_Marshalling.InsurancePlan_Plan_SpecificCost_Benefit), typeof(Hl7.Fhir.Model.InsurancePlan.PlanBenefitComponent) },
            { typeof(FHIR_Marshalling.InsurancePlan_Plan_SpecificCost_Benefit_Cost), typeof(Hl7.Fhir.Model.InsurancePlan.CostComponent) },
            { typeof(FHIR_Marshalling.Invoice), typeof(Hl7.Fhir.Model.Invoice) },
            { typeof(FHIR_Marshalling.Invoice_Participant), typeof(Hl7.Fhir.Model.Invoice.ParticipantComponent) },
            { typeof(FHIR_Marshalling.Invoice_LineItem), typeof(Hl7.Fhir.Model.Invoice.LineItemComponent) },
            { typeof(FHIR_Marshalling.Invoice_LineItem_PriceComponent), typeof(Hl7.Fhir.Model.Invoice.PriceComponentComponent) },
            { typeof(FHIR_Marshalling.Library), typeof(Hl7.Fhir.Model.Library) },
            { typeof(FHIR_Marshalling.Linkage), typeof(Hl7.Fhir.Model.Linkage) },
            { typeof(FHIR_Marshalling.Linkage_Item), typeof(Hl7.Fhir.Model.Linkage.ItemComponent) },
            { typeof(FHIR_Marshalling.List), typeof(Hl7.Fhir.Model.List) },
            { typeof(FHIR_Marshalling.List_Entry), typeof(Hl7.Fhir.Model.List.EntryComponent) },
            { typeof(FHIR_Marshalling.Location), typeof(Hl7.Fhir.Model.Location) },
            { typeof(FHIR_Marshalling.Location_Position), typeof(Hl7.Fhir.Model.Location.PositionComponent) },
            { typeof(FHIR_Marshalling.Location_HoursOfOperation), typeof(Hl7.Fhir.Model.Location.HoursOfOperationComponent) },
            { typeof(FHIR_Marshalling.Measure), typeof(Hl7.Fhir.Model.Measure) },
            { typeof(FHIR_Marshalling.Measure_Group), typeof(Hl7.Fhir.Model.Measure.GroupComponent) },
            { typeof(FHIR_Marshalling.Measure_Group_Population), typeof(Hl7.Fhir.Model.Measure.PopulationComponent) },
            { typeof(FHIR_Marshalling.Measure_Group_Stratifier), typeof(Hl7.Fhir.Model.Measure.StratifierComponent) },
            { typeof(FHIR_Marshalling.Measure_Group_Stratifier_Component), typeof(Hl7.Fhir.Model.Measure.ComponentComponent) },
            { typeof(FHIR_Marshalling.Measure_SupplementalData), typeof(Hl7.Fhir.Model.Measure.SupplementalDataComponent) },
            { typeof(FHIR_Marshalling.MeasureReport), typeof(Hl7.Fhir.Model.MeasureReport) },
            { typeof(FHIR_Marshalling.MeasureReport_Group), typeof(Hl7.Fhir.Model.MeasureReport.GroupComponent) },
            { typeof(FHIR_Marshalling.MeasureReport_Group_Population), typeof(Hl7.Fhir.Model.MeasureReport.PopulationComponent) },
            { typeof(FHIR_Marshalling.MeasureReport_Group_Stratifier), typeof(Hl7.Fhir.Model.MeasureReport.StratifierComponent) },
            { typeof(FHIR_Marshalling.MeasureReport_Group_Stratifier_Stratum), typeof(Hl7.Fhir.Model.MeasureReport.StratifierGroupComponent) },
            { typeof(FHIR_Marshalling.MeasureReport_Group_Stratifier_Stratum_Component), typeof(Hl7.Fhir.Model.MeasureReport.ComponentComponent) },
            { typeof(FHIR_Marshalling.MeasureReport_Group_Stratifier_Stratum_Population), typeof(Hl7.Fhir.Model.MeasureReport.StratifierGroupPopulationComponent) },
            { typeof(FHIR_Marshalling.Media), typeof(Hl7.Fhir.Model.Media) },
            { typeof(FHIR_Marshalling.Medication), typeof(Hl7.Fhir.Model.Medication) },
            { typeof(FHIR_Marshalling.Medication_Ingredient), typeof(Hl7.Fhir.Model.Medication.IngredientComponent) },
            { typeof(FHIR_Marshalling.Medication_Batch), typeof(Hl7.Fhir.Model.Medication.BatchComponent) },
            { typeof(FHIR_Marshalling.MedicationAdministration), typeof(Hl7.Fhir.Model.MedicationAdministration) },
            { typeof(FHIR_Marshalling.MedicationAdministration_Performer), typeof(Hl7.Fhir.Model.MedicationAdministration.PerformerComponent) },
            { typeof(FHIR_Marshalling.MedicationAdministration_Dosage), typeof(Hl7.Fhir.Model.MedicationAdministration.DosageComponent) },
            { typeof(FHIR_Marshalling.MedicationDispense), typeof(Hl7.Fhir.Model.MedicationDispense) },
            { typeof(FHIR_Marshalling.MedicationDispense_Performer), typeof(Hl7.Fhir.Model.MedicationDispense.PerformerComponent) },
            { typeof(FHIR_Marshalling.MedicationDispense_Substitution), typeof(Hl7.Fhir.Model.MedicationDispense.SubstitutionComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge), typeof(Hl7.Fhir.Model.MedicationKnowledge) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_RelatedMedicationKnowledge), typeof(Hl7.Fhir.Model.MedicationKnowledge.RelatedMedicationKnowledgeComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_Monograph), typeof(Hl7.Fhir.Model.MedicationKnowledge.MonographComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_Ingredient), typeof(Hl7.Fhir.Model.MedicationKnowledge.IngredientComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_Cost), typeof(Hl7.Fhir.Model.MedicationKnowledge.CostComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_MonitoringProgram), typeof(Hl7.Fhir.Model.MedicationKnowledge.MonitoringProgramComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_AdministrationGuidelines), typeof(Hl7.Fhir.Model.MedicationKnowledge.AdministrationGuidelinesComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_AdministrationGuidelines_Dosage), typeof(Hl7.Fhir.Model.MedicationKnowledge.DosageComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_AdministrationGuidelines_PatientCharacteristics), typeof(Hl7.Fhir.Model.MedicationKnowledge.PatientCharacteristicsComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_MedicineClassification), typeof(Hl7.Fhir.Model.MedicationKnowledge.MedicineClassificationComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_Packaging), typeof(Hl7.Fhir.Model.MedicationKnowledge.PackagingComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_DrugCharacteristic), typeof(Hl7.Fhir.Model.MedicationKnowledge.DrugCharacteristicComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_Regulatory), typeof(Hl7.Fhir.Model.MedicationKnowledge.RegulatoryComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_Regulatory_Substitution), typeof(Hl7.Fhir.Model.MedicationKnowledge.SubstitutionComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_Regulatory_Schedule), typeof(Hl7.Fhir.Model.MedicationKnowledge.ScheduleComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_Regulatory_MaxDispense), typeof(Hl7.Fhir.Model.MedicationKnowledge.MaxDispenseComponent) },
            { typeof(FHIR_Marshalling.MedicationKnowledge_Kinetics), typeof(Hl7.Fhir.Model.MedicationKnowledge.KineticsComponent) },
            { typeof(FHIR_Marshalling.MedicationRequest), typeof(Hl7.Fhir.Model.MedicationRequest) },
            { typeof(FHIR_Marshalling.MedicationRequest_DispenseRequest), typeof(Hl7.Fhir.Model.MedicationRequest.DispenseRequestComponent) },
            { typeof(FHIR_Marshalling.MedicationRequest_DispenseRequest_InitialFill), typeof(Hl7.Fhir.Model.MedicationRequest.InitialFillComponent) },
            { typeof(FHIR_Marshalling.MedicationRequest_Substitution), typeof(Hl7.Fhir.Model.MedicationRequest.SubstitutionComponent) },
            { typeof(FHIR_Marshalling.MedicationStatement), typeof(Hl7.Fhir.Model.MedicationStatement) },
            { typeof(FHIR_Marshalling.MedicinalProduct), typeof(Hl7.Fhir.Model.MedicinalProduct) },
            { typeof(FHIR_Marshalling.MedicinalProduct_Name), typeof(Hl7.Fhir.Model.MedicinalProduct.NameComponent) },
            { typeof(FHIR_Marshalling.MedicinalProduct_Name_NamePart), typeof(Hl7.Fhir.Model.MedicinalProduct.NamePartComponent) },
            { typeof(FHIR_Marshalling.MedicinalProduct_Name_CountryLanguage), typeof(Hl7.Fhir.Model.MedicinalProduct.CountryLanguageComponent) },
            { typeof(FHIR_Marshalling.MedicinalProduct_ManufacturingBusinessOperation), typeof(Hl7.Fhir.Model.MedicinalProduct.ManufacturingBusinessOperationComponent) },
            { typeof(FHIR_Marshalling.MedicinalProduct_SpecialDesignation), typeof(Hl7.Fhir.Model.MedicinalProduct.SpecialDesignationComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductAuthorization), typeof(Hl7.Fhir.Model.MedicinalProductAuthorization) },
            { typeof(FHIR_Marshalling.MedicinalProductAuthorization_JurisdictionalAuthorization), typeof(Hl7.Fhir.Model.MedicinalProductAuthorization.JurisdictionalAuthorizationComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductAuthorization_Procedure), typeof(Hl7.Fhir.Model.MedicinalProductAuthorization.ProcedureComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductContraindication), typeof(Hl7.Fhir.Model.MedicinalProductContraindication) },
            { typeof(FHIR_Marshalling.MedicinalProductContraindication_OtherTherapy), typeof(Hl7.Fhir.Model.MedicinalProductContraindication.OtherTherapyComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductIndication), typeof(Hl7.Fhir.Model.MedicinalProductIndication) },
            { typeof(FHIR_Marshalling.MedicinalProductIndication_OtherTherapy), typeof(Hl7.Fhir.Model.MedicinalProductIndication.OtherTherapyComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductIngredient), typeof(Hl7.Fhir.Model.MedicinalProductIngredient) },
            { typeof(FHIR_Marshalling.MedicinalProductIngredient_SpecifiedSubstance), typeof(Hl7.Fhir.Model.MedicinalProductIngredient.SpecifiedSubstanceComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductIngredient_SpecifiedSubstance_Strength), typeof(Hl7.Fhir.Model.MedicinalProductIngredient.StrengthComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductIngredient_SpecifiedSubstance_Strength_ReferenceStrength), typeof(Hl7.Fhir.Model.MedicinalProductIngredient.ReferenceStrengthComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductIngredient_Substance), typeof(Hl7.Fhir.Model.MedicinalProductIngredient.SubstanceComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductInteraction), typeof(Hl7.Fhir.Model.MedicinalProductInteraction) },
            { typeof(FHIR_Marshalling.MedicinalProductInteraction_Interactant), typeof(Hl7.Fhir.Model.MedicinalProductInteraction.InteractantComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductManufactured), typeof(Hl7.Fhir.Model.MedicinalProductManufactured) },
            { typeof(FHIR_Marshalling.MedicinalProductPackaged), typeof(Hl7.Fhir.Model.MedicinalProductPackaged) },
            { typeof(FHIR_Marshalling.MedicinalProductPackaged_BatchIdentifier), typeof(Hl7.Fhir.Model.MedicinalProductPackaged.BatchIdentifierComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductPackaged_PackageItem), typeof(Hl7.Fhir.Model.MedicinalProductPackaged.PackageItemComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductPharmaceutical), typeof(Hl7.Fhir.Model.MedicinalProductPharmaceutical) },
            { typeof(FHIR_Marshalling.MedicinalProductPharmaceutical_Characteristics), typeof(Hl7.Fhir.Model.MedicinalProductPharmaceutical.CharacteristicsComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductPharmaceutical_RouteOfAdministration), typeof(Hl7.Fhir.Model.MedicinalProductPharmaceutical.RouteOfAdministrationComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductPharmaceutical_RouteOfAdministration_TargetSpecies), typeof(Hl7.Fhir.Model.MedicinalProductPharmaceutical.TargetSpeciesComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductPharmaceutical_RouteOfAdministration_TargetSpecies_WithdrawalPeriod), typeof(Hl7.Fhir.Model.MedicinalProductPharmaceutical.WithdrawalPeriodComponent) },
            { typeof(FHIR_Marshalling.MedicinalProductUndesirableEffect), typeof(Hl7.Fhir.Model.MedicinalProductUndesirableEffect) },
            { typeof(FHIR_Marshalling.MessageDefinition), typeof(Hl7.Fhir.Model.MessageDefinition) },
            { typeof(FHIR_Marshalling.MessageDefinition_Focus), typeof(Hl7.Fhir.Model.MessageDefinition.FocusComponent) },
            { typeof(FHIR_Marshalling.MessageDefinition_AllowedResponse), typeof(Hl7.Fhir.Model.MessageDefinition.AllowedResponseComponent) },
            { typeof(FHIR_Marshalling.MessageHeader), typeof(Hl7.Fhir.Model.MessageHeader) },
            { typeof(FHIR_Marshalling.MessageHeader_Destination), typeof(Hl7.Fhir.Model.MessageHeader.MessageDestinationComponent) },
            { typeof(FHIR_Marshalling.MessageHeader_Source), typeof(Hl7.Fhir.Model.MessageHeader.MessageSourceComponent) },
            { typeof(FHIR_Marshalling.MessageHeader_Response), typeof(Hl7.Fhir.Model.MessageHeader.ResponseComponent) },
            { typeof(FHIR_Marshalling.MolecularSequence), typeof(Hl7.Fhir.Model.MolecularSequence) },
            { typeof(FHIR_Marshalling.MolecularSequence_ReferenceSeq), typeof(Hl7.Fhir.Model.MolecularSequence.ReferenceSeqComponent) },
            { typeof(FHIR_Marshalling.MolecularSequence_Variant), typeof(Hl7.Fhir.Model.MolecularSequence.VariantComponent) },
            { typeof(FHIR_Marshalling.MolecularSequence_Quality), typeof(Hl7.Fhir.Model.MolecularSequence.QualityComponent) },
            { typeof(FHIR_Marshalling.MolecularSequence_Quality_Roc), typeof(Hl7.Fhir.Model.MolecularSequence.RocComponent) },
            { typeof(FHIR_Marshalling.MolecularSequence_Repository), typeof(Hl7.Fhir.Model.MolecularSequence.RepositoryComponent) },
            { typeof(FHIR_Marshalling.MolecularSequence_StructureVariant), typeof(Hl7.Fhir.Model.MolecularSequence.StructureVariantComponent) },
            { typeof(FHIR_Marshalling.MolecularSequence_StructureVariant_Outer), typeof(Hl7.Fhir.Model.MolecularSequence.OuterComponent) },
            { typeof(FHIR_Marshalling.MolecularSequence_StructureVariant_Inner), typeof(Hl7.Fhir.Model.MolecularSequence.InnerComponent) },
            { typeof(FHIR_Marshalling.NamingSystem), typeof(Hl7.Fhir.Model.NamingSystem) },
            { typeof(FHIR_Marshalling.NamingSystem_UniqueId), typeof(Hl7.Fhir.Model.NamingSystem.UniqueIdComponent) },
            { typeof(FHIR_Marshalling.NutritionOrder), typeof(Hl7.Fhir.Model.NutritionOrder) },
            { typeof(FHIR_Marshalling.NutritionOrder_OralDiet), typeof(Hl7.Fhir.Model.NutritionOrder.OralDietComponent) },
            { typeof(FHIR_Marshalling.NutritionOrder_OralDiet_Nutrient), typeof(Hl7.Fhir.Model.NutritionOrder.NutrientComponent) },
            { typeof(FHIR_Marshalling.NutritionOrder_OralDiet_Texture), typeof(Hl7.Fhir.Model.NutritionOrder.TextureComponent) },
            { typeof(FHIR_Marshalling.NutritionOrder_Supplement), typeof(Hl7.Fhir.Model.NutritionOrder.SupplementComponent) },
            { typeof(FHIR_Marshalling.NutritionOrder_EnteralFormula), typeof(Hl7.Fhir.Model.NutritionOrder.EnteralFormulaComponent) },
            { typeof(FHIR_Marshalling.NutritionOrder_EnteralFormula_Administration), typeof(Hl7.Fhir.Model.NutritionOrder.AdministrationComponent) },
            { typeof(FHIR_Marshalling.Observation), typeof(Hl7.Fhir.Model.Observation) },
            { typeof(FHIR_Marshalling.Observation_ReferenceRange), typeof(Hl7.Fhir.Model.Observation.ReferenceRangeComponent) },
            { typeof(FHIR_Marshalling.Observation_Component), typeof(Hl7.Fhir.Model.Observation.ComponentComponent) },
            { typeof(FHIR_Marshalling.ObservationDefinition), typeof(Hl7.Fhir.Model.ObservationDefinition) },
            { typeof(FHIR_Marshalling.ObservationDefinition_QuantitativeDetails), typeof(Hl7.Fhir.Model.ObservationDefinition.QuantitativeDetailsComponent) },
            { typeof(FHIR_Marshalling.ObservationDefinition_QualifiedInterval), typeof(Hl7.Fhir.Model.ObservationDefinition.QualifiedIntervalComponent) },
            { typeof(FHIR_Marshalling.OperationDefinition), typeof(Hl7.Fhir.Model.OperationDefinition) },
            { typeof(FHIR_Marshalling.OperationDefinition_Parameter), typeof(Hl7.Fhir.Model.OperationDefinition.ParameterComponent) },
            { typeof(FHIR_Marshalling.OperationDefinition_Parameter_Binding), typeof(Hl7.Fhir.Model.OperationDefinition.BindingComponent) },
            { typeof(FHIR_Marshalling.OperationDefinition_Parameter_ReferencedFrom), typeof(Hl7.Fhir.Model.OperationDefinition.ReferencedFromComponent) },
            { typeof(FHIR_Marshalling.OperationDefinition_Overload), typeof(Hl7.Fhir.Model.OperationDefinition.OverloadComponent) },
            { typeof(FHIR_Marshalling.OperationOutcome), typeof(Hl7.Fhir.Model.OperationOutcome) },
            { typeof(FHIR_Marshalling.OperationOutcome_Issue), typeof(Hl7.Fhir.Model.OperationOutcome.IssueComponent) },
            { typeof(FHIR_Marshalling.Organization), typeof(Hl7.Fhir.Model.Organization) },
            { typeof(FHIR_Marshalling.Organization_Contact), typeof(Hl7.Fhir.Model.Organization.ContactComponent) },
            { typeof(FHIR_Marshalling.OrganizationAffiliation), typeof(Hl7.Fhir.Model.OrganizationAffiliation) },
            { typeof(FHIR_Marshalling.Parameters), typeof(Hl7.Fhir.Model.Parameters) },
            { typeof(FHIR_Marshalling.Parameters_Parameter), typeof(Hl7.Fhir.Model.Parameters.ParameterComponent) },
            { typeof(FHIR_Marshalling.Patient), typeof(Hl7.Fhir.Model.Patient) },
            { typeof(FHIR_Marshalling.Patient_Contact), typeof(Hl7.Fhir.Model.Patient.ContactComponent) },
            { typeof(FHIR_Marshalling.Patient_Communication), typeof(Hl7.Fhir.Model.Patient.CommunicationComponent) },
            { typeof(FHIR_Marshalling.Patient_Link), typeof(Hl7.Fhir.Model.Patient.LinkComponent) },
            { typeof(FHIR_Marshalling.PaymentNotice), typeof(Hl7.Fhir.Model.PaymentNotice) },
            { typeof(FHIR_Marshalling.PaymentReconciliation), typeof(Hl7.Fhir.Model.PaymentReconciliation) },
            { typeof(FHIR_Marshalling.PaymentReconciliation_Detail), typeof(Hl7.Fhir.Model.PaymentReconciliation.DetailsComponent) },
            { typeof(FHIR_Marshalling.PaymentReconciliation_ProcessNote), typeof(Hl7.Fhir.Model.PaymentReconciliation.NotesComponent) },
            { typeof(FHIR_Marshalling.Person), typeof(Hl7.Fhir.Model.Person) },
            { typeof(FHIR_Marshalling.Person_Link), typeof(Hl7.Fhir.Model.Person.LinkComponent) },
            { typeof(FHIR_Marshalling.PlanDefinition), typeof(Hl7.Fhir.Model.PlanDefinition) },
            { typeof(FHIR_Marshalling.PlanDefinition_Goal), typeof(Hl7.Fhir.Model.PlanDefinition.GoalComponent) },
            { typeof(FHIR_Marshalling.PlanDefinition_Goal_Target), typeof(Hl7.Fhir.Model.PlanDefinition.TargetComponent) },
            { typeof(FHIR_Marshalling.PlanDefinition_Action), typeof(Hl7.Fhir.Model.PlanDefinition.ActionComponent) },
            { typeof(FHIR_Marshalling.PlanDefinition_Action_Condition), typeof(Hl7.Fhir.Model.PlanDefinition.ConditionComponent) },
            { typeof(FHIR_Marshalling.PlanDefinition_Action_RelatedAction), typeof(Hl7.Fhir.Model.PlanDefinition.RelatedActionComponent) },
            { typeof(FHIR_Marshalling.PlanDefinition_Action_Participant), typeof(Hl7.Fhir.Model.PlanDefinition.ParticipantComponent) },
            { typeof(FHIR_Marshalling.PlanDefinition_Action_DynamicValue), typeof(Hl7.Fhir.Model.PlanDefinition.DynamicValueComponent) },
            { typeof(FHIR_Marshalling.Practitioner), typeof(Hl7.Fhir.Model.Practitioner) },
            { typeof(FHIR_Marshalling.Practitioner_Qualification), typeof(Hl7.Fhir.Model.Practitioner.QualificationComponent) },
            { typeof(FHIR_Marshalling.PractitionerRole), typeof(Hl7.Fhir.Model.PractitionerRole) },
            { typeof(FHIR_Marshalling.PractitionerRole_AvailableTime), typeof(Hl7.Fhir.Model.PractitionerRole.AvailableTimeComponent) },
            { typeof(FHIR_Marshalling.PractitionerRole_NotAvailable), typeof(Hl7.Fhir.Model.PractitionerRole.NotAvailableComponent) },
            { typeof(FHIR_Marshalling.Procedure), typeof(Hl7.Fhir.Model.Procedure) },
            { typeof(FHIR_Marshalling.Procedure_Performer), typeof(Hl7.Fhir.Model.Procedure.PerformerComponent) },
            { typeof(FHIR_Marshalling.Procedure_FocalDevice), typeof(Hl7.Fhir.Model.Procedure.FocalDeviceComponent) },
            { typeof(FHIR_Marshalling.Provenance), typeof(Hl7.Fhir.Model.Provenance) },
            { typeof(FHIR_Marshalling.Provenance_Agent), typeof(Hl7.Fhir.Model.Provenance.AgentComponent) },
            { typeof(FHIR_Marshalling.Provenance_Entity), typeof(Hl7.Fhir.Model.Provenance.EntityComponent) },
            { typeof(FHIR_Marshalling.Questionnaire), typeof(Hl7.Fhir.Model.Questionnaire) },
            { typeof(FHIR_Marshalling.Questionnaire_Item), typeof(Hl7.Fhir.Model.Questionnaire.ItemComponent) },
            { typeof(FHIR_Marshalling.Questionnaire_Item_EnableWhen), typeof(Hl7.Fhir.Model.Questionnaire.EnableWhenComponent) },
            { typeof(FHIR_Marshalling.Questionnaire_Item_AnswerOption), typeof(Hl7.Fhir.Model.Questionnaire.AnswerOptionComponent) },
            { typeof(FHIR_Marshalling.Questionnaire_Item_Initial), typeof(Hl7.Fhir.Model.Questionnaire.InitialComponent) },
            { typeof(FHIR_Marshalling.QuestionnaireResponse), typeof(Hl7.Fhir.Model.QuestionnaireResponse) },
            { typeof(FHIR_Marshalling.QuestionnaireResponse_Item), typeof(Hl7.Fhir.Model.QuestionnaireResponse.ItemComponent) },
            { typeof(FHIR_Marshalling.QuestionnaireResponse_Item_Answer), typeof(Hl7.Fhir.Model.QuestionnaireResponse.AnswerComponent) },
            { typeof(FHIR_Marshalling.RelatedPerson), typeof(Hl7.Fhir.Model.RelatedPerson) },
            { typeof(FHIR_Marshalling.RelatedPerson_Communication), typeof(Hl7.Fhir.Model.RelatedPerson.CommunicationComponent) },
            { typeof(FHIR_Marshalling.RequestGroup), typeof(Hl7.Fhir.Model.RequestGroup) },
            { typeof(FHIR_Marshalling.RequestGroup_Action), typeof(Hl7.Fhir.Model.RequestGroup.ActionComponent) },
            { typeof(FHIR_Marshalling.RequestGroup_Action_Condition), typeof(Hl7.Fhir.Model.RequestGroup.ConditionComponent) },
            { typeof(FHIR_Marshalling.RequestGroup_Action_RelatedAction), typeof(Hl7.Fhir.Model.RequestGroup.RelatedActionComponent) },
            { typeof(FHIR_Marshalling.ResearchDefinition), typeof(Hl7.Fhir.Model.ResearchDefinition) },
            { typeof(FHIR_Marshalling.ResearchElementDefinition), typeof(Hl7.Fhir.Model.ResearchElementDefinition) },
            { typeof(FHIR_Marshalling.ResearchElementDefinition_Characteristic), typeof(Hl7.Fhir.Model.ResearchElementDefinition.CharacteristicComponent) },
            { typeof(FHIR_Marshalling.ResearchStudy), typeof(Hl7.Fhir.Model.ResearchStudy) },
            { typeof(FHIR_Marshalling.ResearchStudy_Arm), typeof(Hl7.Fhir.Model.ResearchStudy.ArmComponent) },
            { typeof(FHIR_Marshalling.ResearchStudy_Objective), typeof(Hl7.Fhir.Model.ResearchStudy.ObjectiveComponent) },
            { typeof(FHIR_Marshalling.ResearchSubject), typeof(Hl7.Fhir.Model.ResearchSubject) },
            { typeof(FHIR_Marshalling.RiskAssessment), typeof(Hl7.Fhir.Model.RiskAssessment) },
            { typeof(FHIR_Marshalling.RiskAssessment_Prediction), typeof(Hl7.Fhir.Model.RiskAssessment.PredictionComponent) },
            { typeof(FHIR_Marshalling.RiskEvidenceSynthesis), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis) },
            { typeof(FHIR_Marshalling.RiskEvidenceSynthesis_SampleSize), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis.SampleSizeComponent) },
            { typeof(FHIR_Marshalling.RiskEvidenceSynthesis_RiskEstimate), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis.RiskEstimateComponent) },
            { typeof(FHIR_Marshalling.RiskEvidenceSynthesis_RiskEstimate_PrecisionEstimate), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis.PrecisionEstimateComponent) },
            { typeof(FHIR_Marshalling.RiskEvidenceSynthesis_Certainty), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis.CertaintyComponent) },
            { typeof(FHIR_Marshalling.RiskEvidenceSynthesis_Certainty_CertaintySubcomponent), typeof(Hl7.Fhir.Model.RiskEvidenceSynthesis.CertaintySubcomponentComponent) },
            { typeof(FHIR_Marshalling.Schedule), typeof(Hl7.Fhir.Model.Schedule) },
            { typeof(FHIR_Marshalling.SearchParameter), typeof(Hl7.Fhir.Model.SearchParameter) },
            { typeof(FHIR_Marshalling.SearchParameter_Component), typeof(Hl7.Fhir.Model.SearchParameter.ComponentComponent) },
            { typeof(FHIR_Marshalling.ServiceRequest), typeof(Hl7.Fhir.Model.ServiceRequest) },
            { typeof(FHIR_Marshalling.Slot), typeof(Hl7.Fhir.Model.Slot) },
            { typeof(FHIR_Marshalling.Specimen), typeof(Hl7.Fhir.Model.Specimen) },
            { typeof(FHIR_Marshalling.Specimen_Collection), typeof(Hl7.Fhir.Model.Specimen.CollectionComponent) },
            { typeof(FHIR_Marshalling.Specimen_Processing), typeof(Hl7.Fhir.Model.Specimen.ProcessingComponent) },
            { typeof(FHIR_Marshalling.Specimen_Container), typeof(Hl7.Fhir.Model.Specimen.ContainerComponent) },
            { typeof(FHIR_Marshalling.SpecimenDefinition), typeof(Hl7.Fhir.Model.SpecimenDefinition) },
            { typeof(FHIR_Marshalling.SpecimenDefinition_TypeTested), typeof(Hl7.Fhir.Model.SpecimenDefinition.TypeTestedComponent) },
            { typeof(FHIR_Marshalling.SpecimenDefinition_TypeTested_Container), typeof(Hl7.Fhir.Model.SpecimenDefinition.ContainerComponent) },
            { typeof(FHIR_Marshalling.SpecimenDefinition_TypeTested_Container_Additive), typeof(Hl7.Fhir.Model.SpecimenDefinition.AdditiveComponent) },
            { typeof(FHIR_Marshalling.SpecimenDefinition_TypeTested_Handling), typeof(Hl7.Fhir.Model.SpecimenDefinition.HandlingComponent) },
            { typeof(FHIR_Marshalling.StructureDefinition), typeof(Hl7.Fhir.Model.StructureDefinition) },
            { typeof(FHIR_Marshalling.StructureDefinition_Mapping), typeof(Hl7.Fhir.Model.StructureDefinition.MappingComponent) },
            { typeof(FHIR_Marshalling.StructureDefinition_Context), typeof(Hl7.Fhir.Model.StructureDefinition.ContextComponent) },
            { typeof(FHIR_Marshalling.StructureDefinition_Snapshot), typeof(Hl7.Fhir.Model.StructureDefinition.SnapshotComponent) },
            { typeof(FHIR_Marshalling.StructureDefinition_Differential), typeof(Hl7.Fhir.Model.StructureDefinition.DifferentialComponent) },
            { typeof(FHIR_Marshalling.StructureMap), typeof(Hl7.Fhir.Model.StructureMap) },
            { typeof(FHIR_Marshalling.StructureMap_Structure), typeof(Hl7.Fhir.Model.StructureMap.StructureComponent) },
            { typeof(FHIR_Marshalling.StructureMap_Group), typeof(Hl7.Fhir.Model.StructureMap.GroupComponent) },
            { typeof(FHIR_Marshalling.StructureMap_Group_Input), typeof(Hl7.Fhir.Model.StructureMap.InputComponent) },
            { typeof(FHIR_Marshalling.StructureMap_Group_Rule), typeof(Hl7.Fhir.Model.StructureMap.RuleComponent) },
            { typeof(FHIR_Marshalling.StructureMap_Group_Rule_Source), typeof(Hl7.Fhir.Model.StructureMap.SourceComponent) },
            { typeof(FHIR_Marshalling.StructureMap_Group_Rule_Target), typeof(Hl7.Fhir.Model.StructureMap.TargetComponent) },
            { typeof(FHIR_Marshalling.StructureMap_Group_Rule_Target_Parameter), typeof(Hl7.Fhir.Model.StructureMap.ParameterComponent) },
            { typeof(FHIR_Marshalling.StructureMap_Group_Rule_Dependent), typeof(Hl7.Fhir.Model.StructureMap.DependentComponent) },
            { typeof(FHIR_Marshalling.Subscription), typeof(Hl7.Fhir.Model.Subscription) },
            { typeof(FHIR_Marshalling.Subscription_Channel), typeof(Hl7.Fhir.Model.Subscription.ChannelComponent) },
            { typeof(FHIR_Marshalling.Substance), typeof(Hl7.Fhir.Model.Substance) },
            { typeof(FHIR_Marshalling.Substance_Instance), typeof(Hl7.Fhir.Model.Substance.InstanceComponent) },
            { typeof(FHIR_Marshalling.Substance_Ingredient), typeof(Hl7.Fhir.Model.Substance.IngredientComponent) },
            { typeof(FHIR_Marshalling.SubstanceNucleicAcid), typeof(Hl7.Fhir.Model.SubstanceNucleicAcid) },
            { typeof(FHIR_Marshalling.SubstanceNucleicAcid_Subunit), typeof(Hl7.Fhir.Model.SubstanceNucleicAcid.SubunitComponent) },
            { typeof(FHIR_Marshalling.SubstanceNucleicAcid_Subunit_Linkage), typeof(Hl7.Fhir.Model.SubstanceNucleicAcid.LinkageComponent) },
            { typeof(FHIR_Marshalling.SubstanceNucleicAcid_Subunit_Sugar), typeof(Hl7.Fhir.Model.SubstanceNucleicAcid.SugarComponent) },
            { typeof(FHIR_Marshalling.SubstancePolymer), typeof(Hl7.Fhir.Model.SubstancePolymer) },
            { typeof(FHIR_Marshalling.SubstancePolymer_MonomerSet), typeof(Hl7.Fhir.Model.SubstancePolymer.MonomerSetComponent) },
            { typeof(FHIR_Marshalling.SubstancePolymer_MonomerSet_StartingMaterial), typeof(Hl7.Fhir.Model.SubstancePolymer.StartingMaterialComponent) },
            { typeof(FHIR_Marshalling.SubstancePolymer_Repeat), typeof(Hl7.Fhir.Model.SubstancePolymer.RepeatComponent) },
            { typeof(FHIR_Marshalling.SubstancePolymer_Repeat_RepeatUnit), typeof(Hl7.Fhir.Model.SubstancePolymer.RepeatUnitComponent) },
            { typeof(FHIR_Marshalling.SubstancePolymer_Repeat_RepeatUnit_DegreeOfPolymerisation), typeof(Hl7.Fhir.Model.SubstancePolymer.DegreeOfPolymerisationComponent) },
            { typeof(FHIR_Marshalling.SubstancePolymer_Repeat_RepeatUnit_StructuralRepresentation), typeof(Hl7.Fhir.Model.SubstancePolymer.StructuralRepresentationComponent) },
            { typeof(FHIR_Marshalling.SubstanceProtein), typeof(Hl7.Fhir.Model.SubstanceProtein) },
            { typeof(FHIR_Marshalling.SubstanceProtein_Subunit), typeof(Hl7.Fhir.Model.SubstanceProtein.SubunitComponent) },
            { typeof(FHIR_Marshalling.SubstanceReferenceInformation), typeof(Hl7.Fhir.Model.SubstanceReferenceInformation) },
            { typeof(FHIR_Marshalling.SubstanceReferenceInformation_Gene), typeof(Hl7.Fhir.Model.SubstanceReferenceInformation.GeneComponent) },
            { typeof(FHIR_Marshalling.SubstanceReferenceInformation_GeneElement), typeof(Hl7.Fhir.Model.SubstanceReferenceInformation.GeneElementComponent) },
            { typeof(FHIR_Marshalling.SubstanceReferenceInformation_Classification), typeof(Hl7.Fhir.Model.SubstanceReferenceInformation.ClassificationComponent) },
            { typeof(FHIR_Marshalling.SubstanceReferenceInformation_Target), typeof(Hl7.Fhir.Model.SubstanceReferenceInformation.TargetComponent) },
            { typeof(FHIR_Marshalling.SubstanceSourceMaterial), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial) },
            { typeof(FHIR_Marshalling.SubstanceSourceMaterial_FractionDescription), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.FractionDescriptionComponent) },
            { typeof(FHIR_Marshalling.SubstanceSourceMaterial_Organism), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.OrganismComponent) },
            { typeof(FHIR_Marshalling.SubstanceSourceMaterial_Organism_Author), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.AuthorComponent) },
            { typeof(FHIR_Marshalling.SubstanceSourceMaterial_Organism_Hybrid), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.HybridComponent) },
            { typeof(FHIR_Marshalling.SubstanceSourceMaterial_Organism_OrganismGeneral), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.OrganismGeneralComponent) },
            { typeof(FHIR_Marshalling.SubstanceSourceMaterial_PartDescription), typeof(Hl7.Fhir.Model.SubstanceSourceMaterial.PartDescriptionComponent) },
            { typeof(FHIR_Marshalling.SubstanceSpecification), typeof(Hl7.Fhir.Model.SubstanceSpecification) },
            { typeof(FHIR_Marshalling.SubstanceSpecification_Moiety), typeof(Hl7.Fhir.Model.SubstanceSpecification.MoietyComponent) },
            { typeof(FHIR_Marshalling.SubstanceSpecification_Property), typeof(Hl7.Fhir.Model.SubstanceSpecification.PropertyComponent) },
            { typeof(FHIR_Marshalling.SubstanceSpecification_Structure), typeof(Hl7.Fhir.Model.SubstanceSpecification.StructureComponent) },
            { typeof(FHIR_Marshalling.SubstanceSpecification_Structure_Isotope), typeof(Hl7.Fhir.Model.SubstanceSpecification.IsotopeComponent) },
            { typeof(FHIR_Marshalling.SubstanceSpecification_Structure_Isotope_MolecularWeight), typeof(Hl7.Fhir.Model.SubstanceSpecification.MolecularWeightComponent) },
            { typeof(FHIR_Marshalling.SubstanceSpecification_Structure_Representation), typeof(Hl7.Fhir.Model.SubstanceSpecification.RepresentationComponent) },
            { typeof(FHIR_Marshalling.SubstanceSpecification_Code), typeof(Hl7.Fhir.Model.SubstanceSpecification.CodeComponent) },
            { typeof(FHIR_Marshalling.SubstanceSpecification_Name), typeof(Hl7.Fhir.Model.SubstanceSpecification.NameComponent) },
            { typeof(FHIR_Marshalling.SubstanceSpecification_Name_Official), typeof(Hl7.Fhir.Model.SubstanceSpecification.OfficialComponent) },
            { typeof(FHIR_Marshalling.SubstanceSpecification_Relationship), typeof(Hl7.Fhir.Model.SubstanceSpecification.RelationshipComponent) },
            { typeof(FHIR_Marshalling.SupplyDelivery), typeof(Hl7.Fhir.Model.SupplyDelivery) },
            { typeof(FHIR_Marshalling.SupplyDelivery_SuppliedItem), typeof(Hl7.Fhir.Model.SupplyDelivery.SuppliedItemComponent) },
            { typeof(FHIR_Marshalling.SupplyRequest), typeof(Hl7.Fhir.Model.SupplyRequest) },
            { typeof(FHIR_Marshalling.SupplyRequest_Parameter), typeof(Hl7.Fhir.Model.SupplyRequest.ParameterComponent) },
            { typeof(FHIR_Marshalling.Task), typeof(Hl7.Fhir.Model.Task) },
            { typeof(FHIR_Marshalling.Task_Restriction), typeof(Hl7.Fhir.Model.Task.RestrictionComponent) },
            { typeof(FHIR_Marshalling.Task_Input), typeof(Hl7.Fhir.Model.Task.ParameterComponent) },
            { typeof(FHIR_Marshalling.Task_Output), typeof(Hl7.Fhir.Model.Task.OutputComponent) },
            { typeof(FHIR_Marshalling.TerminologyCapabilities), typeof(Hl7.Fhir.Model.TerminologyCapabilities) },
            { typeof(FHIR_Marshalling.TerminologyCapabilities_Software), typeof(Hl7.Fhir.Model.TerminologyCapabilities.SoftwareComponent) },
            { typeof(FHIR_Marshalling.TerminologyCapabilities_Implementation), typeof(Hl7.Fhir.Model.TerminologyCapabilities.ImplementationComponent) },
            { typeof(FHIR_Marshalling.TerminologyCapabilities_CodeSystem), typeof(Hl7.Fhir.Model.TerminologyCapabilities.CodeSystemComponent) },
            { typeof(FHIR_Marshalling.TerminologyCapabilities_CodeSystem_Version), typeof(Hl7.Fhir.Model.TerminologyCapabilities.VersionComponent) },
            { typeof(FHIR_Marshalling.TerminologyCapabilities_CodeSystem_Version_Filter), typeof(Hl7.Fhir.Model.TerminologyCapabilities.FilterComponent) },
            { typeof(FHIR_Marshalling.TerminologyCapabilities_Expansion), typeof(Hl7.Fhir.Model.TerminologyCapabilities.ExpansionComponent) },
            { typeof(FHIR_Marshalling.TerminologyCapabilities_Expansion_Parameter), typeof(Hl7.Fhir.Model.TerminologyCapabilities.ParameterComponent) },
            { typeof(FHIR_Marshalling.TerminologyCapabilities_ValidateCode), typeof(Hl7.Fhir.Model.TerminologyCapabilities.ValidateCodeComponent) },
            { typeof(FHIR_Marshalling.TerminologyCapabilities_Translation), typeof(Hl7.Fhir.Model.TerminologyCapabilities.TranslationComponent) },
            { typeof(FHIR_Marshalling.TerminologyCapabilities_Closure), typeof(Hl7.Fhir.Model.TerminologyCapabilities.ClosureComponent) },
            { typeof(FHIR_Marshalling.TestReport), typeof(Hl7.Fhir.Model.TestReport) },
            { typeof(FHIR_Marshalling.TestReport_Participant), typeof(Hl7.Fhir.Model.TestReport.ParticipantComponent) },
            { typeof(FHIR_Marshalling.TestReport_Setup), typeof(Hl7.Fhir.Model.TestReport.SetupComponent) },
            { typeof(FHIR_Marshalling.TestReport_Setup_Action), typeof(Hl7.Fhir.Model.TestReport.SetupActionComponent) },
            { typeof(FHIR_Marshalling.TestReport_Setup_Action_Operation), typeof(Hl7.Fhir.Model.TestReport.OperationComponent) },
            { typeof(FHIR_Marshalling.TestReport_Setup_Action_Assert), typeof(Hl7.Fhir.Model.TestReport.AssertComponent) },
            { typeof(FHIR_Marshalling.TestReport_Test), typeof(Hl7.Fhir.Model.TestReport.TestComponent) },
            { typeof(FHIR_Marshalling.TestReport_Test_Action), typeof(Hl7.Fhir.Model.TestReport.TestActionComponent) },
            { typeof(FHIR_Marshalling.TestReport_Teardown), typeof(Hl7.Fhir.Model.TestReport.TeardownComponent) },
            { typeof(FHIR_Marshalling.TestReport_Teardown_Action), typeof(Hl7.Fhir.Model.TestReport.TeardownActionComponent) },
            { typeof(FHIR_Marshalling.TestScript), typeof(Hl7.Fhir.Model.TestScript) },
            { typeof(FHIR_Marshalling.TestScript_Origin), typeof(Hl7.Fhir.Model.TestScript.OriginComponent) },
            { typeof(FHIR_Marshalling.TestScript_Destination), typeof(Hl7.Fhir.Model.TestScript.DestinationComponent) },
            { typeof(FHIR_Marshalling.TestScript_Metadata), typeof(Hl7.Fhir.Model.TestScript.MetadataComponent) },
            { typeof(FHIR_Marshalling.TestScript_Metadata_Link), typeof(Hl7.Fhir.Model.TestScript.LinkComponent) },
            { typeof(FHIR_Marshalling.TestScript_Metadata_Capability), typeof(Hl7.Fhir.Model.TestScript.CapabilityComponent) },
            { typeof(FHIR_Marshalling.TestScript_Fixture), typeof(Hl7.Fhir.Model.TestScript.FixtureComponent) },
            { typeof(FHIR_Marshalling.TestScript_Variable), typeof(Hl7.Fhir.Model.TestScript.VariableComponent) },
            { typeof(FHIR_Marshalling.TestScript_Setup), typeof(Hl7.Fhir.Model.TestScript.SetupComponent) },
            { typeof(FHIR_Marshalling.TestScript_Setup_Action), typeof(Hl7.Fhir.Model.TestScript.SetupActionComponent) },
            { typeof(FHIR_Marshalling.TestScript_Setup_Action_Operation), typeof(Hl7.Fhir.Model.TestScript.OperationComponent) },
            { typeof(FHIR_Marshalling.TestScript_Setup_Action_Operation_RequestHeader), typeof(Hl7.Fhir.Model.TestScript.RequestHeaderComponent) },
            { typeof(FHIR_Marshalling.TestScript_Setup_Action_Assert), typeof(Hl7.Fhir.Model.TestScript.AssertComponent) },
            { typeof(FHIR_Marshalling.TestScript_Test), typeof(Hl7.Fhir.Model.TestScript.TestComponent) },
            { typeof(FHIR_Marshalling.TestScript_Test_Action), typeof(Hl7.Fhir.Model.TestScript.TestActionComponent) },
            { typeof(FHIR_Marshalling.TestScript_Teardown), typeof(Hl7.Fhir.Model.TestScript.TeardownComponent) },
            { typeof(FHIR_Marshalling.TestScript_Teardown_Action), typeof(Hl7.Fhir.Model.TestScript.TeardownActionComponent) },
            { typeof(FHIR_Marshalling.ValueSet), typeof(Hl7.Fhir.Model.ValueSet) },
            { typeof(FHIR_Marshalling.ValueSet_Compose), typeof(Hl7.Fhir.Model.ValueSet.ComposeComponent) },
            { typeof(FHIR_Marshalling.ValueSet_Compose_Include), typeof(Hl7.Fhir.Model.ValueSet.ConceptSetComponent) },
            { typeof(FHIR_Marshalling.ValueSet_Compose_Include_Concept), typeof(Hl7.Fhir.Model.ValueSet.ConceptReferenceComponent) },
            { typeof(FHIR_Marshalling.ValueSet_Compose_Include_Concept_Designation), typeof(Hl7.Fhir.Model.ValueSet.DesignationComponent) },
            { typeof(FHIR_Marshalling.ValueSet_Compose_Include_Filter), typeof(Hl7.Fhir.Model.ValueSet.FilterComponent) },
            { typeof(FHIR_Marshalling.ValueSet_Expansion), typeof(Hl7.Fhir.Model.ValueSet.ExpansionComponent) },
            { typeof(FHIR_Marshalling.ValueSet_Expansion_Parameter), typeof(Hl7.Fhir.Model.ValueSet.ParameterComponent) },
            { typeof(FHIR_Marshalling.ValueSet_Expansion_Contains), typeof(Hl7.Fhir.Model.ValueSet.ContainsComponent) },
            { typeof(FHIR_Marshalling.VerificationResult), typeof(Hl7.Fhir.Model.VerificationResult) },
            { typeof(FHIR_Marshalling.VerificationResult_PrimarySource), typeof(Hl7.Fhir.Model.VerificationResult.PrimarySourceComponent) },
            { typeof(FHIR_Marshalling.VerificationResult_Attestation), typeof(Hl7.Fhir.Model.VerificationResult.AttestationComponent) },
            { typeof(FHIR_Marshalling.VerificationResult_Validator), typeof(Hl7.Fhir.Model.VerificationResult.ValidatorComponent) },
            { typeof(FHIR_Marshalling.VisionPrescription), typeof(Hl7.Fhir.Model.VisionPrescription) },
            { typeof(FHIR_Marshalling.VisionPrescription_LensSpecification), typeof(Hl7.Fhir.Model.VisionPrescription.LensSpecificationComponent) },
            { typeof(FHIR_Marshalling.VisionPrescription_LensSpecification_Prism), typeof(Hl7.Fhir.Model.VisionPrescription.PrismComponent) },
            // NOTE(agw): doesn't seem to be used
            //{ typeof(MetadataResource), typeof(Hl7.Fhir.Model.MetadataResource) },
            // NOTE(agw): abstract
            //{ typeof(Element), typeof(Hl7.Fhir.Model.Element) },
            //{ typeof(BackboneElement), typeof(Hl7.Fhir.Model.BackboneElement) },
            { typeof(FHIR_Marshalling.Address), typeof(Hl7.Fhir.Model.Address) },
            { typeof(FHIR_Marshalling.Age), typeof(Hl7.Fhir.Model.Age) },
            { typeof(FHIR_Marshalling.Annotation), typeof(Hl7.Fhir.Model.Annotation) },
            { typeof(FHIR_Marshalling.Attachment), typeof(Hl7.Fhir.Model.Attachment) },
            { typeof(FHIR_Marshalling.CodeableConcept), typeof(Hl7.Fhir.Model.CodeableConcept) },
            { typeof(FHIR_Marshalling.Coding), typeof(Hl7.Fhir.Model.Coding) },
            { typeof(FHIR_Marshalling.ContactDetail), typeof(Hl7.Fhir.Model.ContactDetail) },
            { typeof(FHIR_Marshalling.ContactPoint), typeof(Hl7.Fhir.Model.ContactPoint) },
            { typeof(FHIR_Marshalling.Contributor), typeof(Hl7.Fhir.Model.Contributor) },
            { typeof(FHIR_Marshalling.Count), typeof(Hl7.Fhir.Model.Count) },
            { typeof(FHIR_Marshalling.DataRequirement), typeof(Hl7.Fhir.Model.DataRequirement) },
            { typeof(FHIR_Marshalling.DataRequirement_CodeFilter), typeof(Hl7.Fhir.Model.DataRequirement.CodeFilterComponent) },
            { typeof(FHIR_Marshalling.DataRequirement_DateFilter), typeof(Hl7.Fhir.Model.DataRequirement.DateFilterComponent) },
            { typeof(FHIR_Marshalling.DataRequirement_Sort), typeof(Hl7.Fhir.Model.DataRequirement.SortComponent) },
            { typeof(FHIR_Marshalling.Distance), typeof(Hl7.Fhir.Model.Distance) },
            { typeof(FHIR_Marshalling.Dosage), typeof(Hl7.Fhir.Model.Dosage) },
            { typeof(FHIR_Marshalling.Dosage_DoseAndRate), typeof(Hl7.Fhir.Model.Dosage.DoseAndRateComponent) },
            { typeof(FHIR_Marshalling.Duration), typeof(Hl7.Fhir.Model.Duration) },
            { typeof(FHIR_Marshalling.ElementDefinition), typeof(Hl7.Fhir.Model.ElementDefinition) },
            { typeof(FHIR_Marshalling.ElementDefinition_Slicing), typeof(Hl7.Fhir.Model.ElementDefinition.SlicingComponent) },
            { typeof(FHIR_Marshalling.ElementDefinition_Slicing_Discriminator), typeof(Hl7.Fhir.Model.ElementDefinition.DiscriminatorComponent) },
            { typeof(FHIR_Marshalling.ElementDefinition_Base), typeof(Hl7.Fhir.Model.ElementDefinition.BaseComponent) },
            { typeof(FHIR_Marshalling.ElementDefinition_Type), typeof(Hl7.Fhir.Model.ElementDefinition.TypeRefComponent) },
            { typeof(FHIR_Marshalling.ElementDefinition_Example), typeof(Hl7.Fhir.Model.ElementDefinition.ExampleComponent) },
            { typeof(FHIR_Marshalling.ElementDefinition_Constraint), typeof(Hl7.Fhir.Model.ElementDefinition.ConstraintComponent) },
            { typeof(FHIR_Marshalling.ElementDefinition_Binding), typeof(Hl7.Fhir.Model.ElementDefinition.ElementDefinitionBindingComponent) },
            { typeof(FHIR_Marshalling.ElementDefinition_Mapping), typeof(Hl7.Fhir.Model.ElementDefinition.MappingComponent) },
            { typeof(FHIR_Marshalling.Expression), typeof(Hl7.Fhir.Model.Expression) },
            { typeof(FHIR_Marshalling.Extension), typeof(Hl7.Fhir.Model.Extension) },
            { typeof(FHIR_Marshalling.HumanName), typeof(Hl7.Fhir.Model.HumanName) },
            { typeof(FHIR_Marshalling.Identifier), typeof(Hl7.Fhir.Model.Identifier) },
            { typeof(FHIR_Marshalling.MarketingStatus), typeof(Hl7.Fhir.Model.MarketingStatus) },
            { typeof(FHIR_Marshalling.Meta), typeof(Hl7.Fhir.Model.Meta) },
            { typeof(FHIR_Marshalling.Money), typeof(Hl7.Fhir.Model.Money) },
            { typeof(FHIR_Marshalling.Narrative), typeof(Hl7.Fhir.Model.Narrative) },
            { typeof(FHIR_Marshalling.ParameterDefinition), typeof(Hl7.Fhir.Model.ParameterDefinition) },
            { typeof(FHIR_Marshalling.Period), typeof(Hl7.Fhir.Model.Period) },
            { typeof(FHIR_Marshalling.Population), typeof(Hl7.Fhir.Model.Population) },
            { typeof(FHIR_Marshalling.ProdCharacteristic), typeof(Hl7.Fhir.Model.ProdCharacteristic) },
            { typeof(FHIR_Marshalling.ProductShelfLife), typeof(Hl7.Fhir.Model.ProductShelfLife) },
            { typeof(FHIR_Marshalling.Quantity), typeof(Hl7.Fhir.Model.Quantity) },
            { typeof(FHIR_Marshalling.Range), typeof(Hl7.Fhir.Model.Range) },
            { typeof(FHIR_Marshalling.Ratio), typeof(Hl7.Fhir.Model.Ratio) },
            { typeof(FHIR_Marshalling.Reference), typeof(Hl7.Fhir.Model.ResourceReference) },
            { typeof(FHIR_Marshalling.RelatedArtifact), typeof(Hl7.Fhir.Model.RelatedArtifact) },
            { typeof(FHIR_Marshalling.SampledData), typeof(Hl7.Fhir.Model.SampledData) },
            { typeof(FHIR_Marshalling.Signature), typeof(Hl7.Fhir.Model.Signature) },
            { typeof(FHIR_Marshalling.SubstanceAmount), typeof(Hl7.Fhir.Model.SubstanceAmount) },
            { typeof(FHIR_Marshalling.SubstanceAmount_ReferenceRange), typeof(Hl7.Fhir.Model.SubstanceAmount.ReferenceRangeComponent) },
            { typeof(FHIR_Marshalling.Timing), typeof(Hl7.Fhir.Model.Timing) },
            { typeof(FHIR_Marshalling.Timing_Repeat), typeof(Hl7.Fhir.Model.Timing.RepeatComponent) },
            { typeof(FHIR_Marshalling.TriggerDefinition), typeof(Hl7.Fhir.Model.TriggerDefinition) },
            { typeof(FHIR_Marshalling.UsageContext), typeof(Hl7.Fhir.Model.UsageContext) },
            // NOTE(agw): could not find anything "moneyquantity" in firely sdk
            //{ typeof(MoneyQuantity), typeof(Hl7.Fhir.Model.Money) },
            { typeof(FHIR_Marshalling.SimpleQuantity), typeof(Hl7.Fhir.Model.Quantity) },
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
