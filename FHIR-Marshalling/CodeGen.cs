using Hl7.Fhir.Model;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using static FHIR_Marshalling.MainClass;

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

        public static bool IsNativePrimative(Type type)
        {
            return type == typeof(String8) || 
                type == typeof(String8) || 
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
            new ConversionEntry{ FirelyType = typeof(FhirBoolean), ExpectedNativeType = typeof(NullableBoolean), SingleFormatString = "new FhirBoolean({0}.GetValue())" },
            new ConversionEntry{ FirelyType = typeof(decimal), ExpectedNativeType = typeof(String8), SingleFormatString = "{0}.DecimalValue()" },
            new ConversionEntry{ FirelyType = typeof(string), ExpectedNativeType = typeof(String8), SingleFormatString = "{0}.ToString()" },
            new ConversionEntry{ FirelyType = typeof(FhirDecimal), ExpectedNativeType = typeof(String8), SingleFormatString = "new FhirDecimal({0}.DecimalValue())" },
            new ConversionEntry{ FirelyType = typeof(Integer), ExpectedNativeType = typeof(NullableInt32), SingleFormatString = $"new Integer({{0}}.{nameof(NullableInt32.GetValue)}())" },
            new ConversionEntry{ FirelyType = typeof(PositiveInt), ExpectedNativeType = typeof(NullableInt32), SingleFormatString = $"new PositiveInt({{0}}.{nameof(NullableInt32.GetValue)}())" },
            new ConversionEntry{ FirelyType = typeof(UnsignedInt), ExpectedNativeType = typeof(NullableInt32), SingleFormatString = $"new UnsignedInt({{0}}.{nameof(NullableInt32.GetValue)}())" },
            new ConversionEntry{ FirelyType = typeof(Integer64), ExpectedNativeType = typeof(long), SingleFormatString = $"new Integer64({{0}}.{nameof(NullableInt32.GetValue)}())" },
        };


        // TODO(agw): combine this into one
        public static Dictionary<Type, Type> CSTypeFromNativeType = new Dictionary<Type, Type>()
        {
            {typeof(String8), typeof(string) },
            {typeof(int), typeof(int) },
            {typeof(NullableInt32), typeof(int?) },
        };

        public static string CSPrimativeFromNativePrimative(Type nativeType, string accessor)
        {
            if(nativeType == typeof(String8))
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
                    return $"{nativeAccessor}";
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
                builder.AppendLine($"Base64Binary {primativeName} = null;");
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
                if(!nativeToFirely.ContainsKey(nativeType)) { throw new NotImplementedException(); }

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

            /*
            string firelyInstantiate = $"var {firelyInstance} = new {firelyTypeFullName}();";
            builder.AppendLine(firelyInstantiate);
            */
            string firelyInstantiate = $"var {firelyInstance} = ({firelyTypeFullName}) FormatterServices.GetUninitializedObject(typeof({firelyTypeFullName}));";
            builder.AppendLine(firelyInstantiate);

            int index = -1;
            foreach (var member in info.Members)
            {
                index++;
                if (member.NativeType == null) continue;
                    // liquidate fields
                switch(member.MemberType)
                {
                    case MemberTypeEnum.Class:
                    case MemberTypeEnum.Primative:
                        {
                            string nativeFieldAccessor = AccessorForNative("in_native", nativeType, member.NativeField);
                            if(member.FirelyType == typeof(Base64Binary))
                            {
                                var primativeName = AppendValueCreation(builder, member.NativeType, member.NativeFieldName, nativeFieldAccessor, member.FirelyType);
                                builder.AppendLine($"{firelyInstance}.{member.FirelyFieldName} = {primativeName};");
                                builder.AppendLine("");
                            }
                            else if (IsNativePrimative(member.NativeType))
                            {
                                if (HasDirectConversion(member.FirelyType))
                                {
                                    string firelyFromCS = FirelyPrimativeFromNativePrimative(member.NativeType, member.FirelyType, nativeFieldAccessor);
                                    string tempName = $"{member.NativeFieldName}_temp";
                                    builder.AppendLine($"var {tempName} = {firelyFromCS};");
                                    builder.AppendLine($"if({tempName} != null) {{");
                                    builder.IndentedAmount += 1;

                                    builder.AppendLine($"{firelyInstance}.{member.FirelyFieldName} = {tempName};");

                                    builder.IndentedAmount -= 1;
                                    builder.AppendLine($"}}");
                                    builder.AppendLine("");
                                }
                                else
                                {
                                    if (member.NativeType == member.FirelyType)
                                    {
                                        builder.AppendLine($"{firelyInstance}.{member.FirelyFieldName} = {nativeFieldAccessor};");
                                        builder.AppendLine("");
                                    }
                                    else if (CSTypeFromNativeType.TryGetValue(member.NativeType, out Type csType))
                                    {
                                        if (csType == member.FirelyType)
                                        {
                                            builder.AppendLine($"{firelyInstance}.{member.FirelyFieldName} = {nativeFieldAccessor};");
                                            builder.AppendLine("");
                                        }

                                        var constructor = member.FirelyType.GetConstructor(new Type[] { csType });
                                        if (constructor != null)
                                        {
                                            var csPrimative = CSPrimativeFromNativePrimative(member.NativeType, nativeFieldAccessor);
                                            string tempName = $"{member.NativeFieldName}_temp";
                                            builder.AppendLine($"var {tempName} = {csPrimative};");
                                            builder.AppendLine($"if({tempName} != null) {{");
                                            builder.IndentedAmount += 1;

                                            builder.AppendLine($"{firelyInstance}.{member.FirelyFieldName} = new {member.FirelyType.FullName}({tempName});");

                                            builder.IndentedAmount -= 1;
                                            builder.AppendLine($"}}");
                                            builder.AppendLine("");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (!nativeToFirely.ContainsKey(member.NativeType)) { throw new NotImplementedException(); }

                                string className = member.NativeType.Name;

                                builder.AppendLine($"if({nativeFieldAccessor} != null) {{");
                                builder.IndentedAmount += 1;

                                string deserializeCall = $"Marshal_{className}({nativeFieldAccessor})";
                                builder.AppendLine($"{firelyInstance}.{member.FirelyFieldName} = {deserializeCall};");

                                builder.IndentedAmount -= 1;
                                builder.AppendLine($"}}");
                                builder.AppendLine("");
                            }
                        }
                        break;
                        
                    case MemberTypeEnum.Enum:
                    {
                            var underlying = member.FirelyType.GetGenericArguments()[0];
                            string underlyingFullName = underlying.FullName.Replace("+", ".");

                            string nativeFieldAccessor = AccessorForNative("in_native", nativeType, member.NativeField);
                            builder.AppendLine($"switch({nativeFieldAccessor}.ToString()) {{");
                            builder.IndentedAmount += 1;
                            foreach (var entry in member.EnumEntries)
                            {
                                builder.AppendLine($"case \"{entry.EnumLiteral.Literal}\": {{");
                                builder.IndentedAmount += 1;

                                var newCode = $"new Code<{underlyingFullName}> ()";
                                builder.AppendLine($"{firelyInstance}.{member.FirelyFieldName} = {newCode};");
                                builder.AppendLine($"{firelyInstance}.{member.FirelyFieldName}.ObjectValue = {underlyingFullName}.{entry.Value};");

                                builder.IndentedAmount -= 1;
                                builder.AppendLine($"}} break;");
                            }

                            builder.IndentedAmount -= 1;
                            builder.AppendLine("}");
                        }
                        break;


                    case MemberTypeEnum.Union:
                    {
                            /*
                             * switch({native.EnumValue}) {
                             *      case {native.enum0}:
                             *      {
                             *         {firely.matchingField} = {convertFunctionName}({native.union.0thElementName});
                             *      } break;
                             * 
                             * }
                             */
                            // NOTE(agw): for each possible type, add a case to the switch statement.
                            // This should switch on the enum value


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
                                var primativeName = AppendValueCreation(builder, entry.NativeType, member.NativeFieldName, unionFieldAccessor, entry.FirelyType);
                                builder.AppendLine($"{firelyInstance}.{member.FirelyFieldName} = {primativeName};");

                                builder.IndentedAmount -= 1;
                                builder.AppendLine($"}} break;");
                            }

                            builder.IndentedAmount -= 1;
                            builder.AppendLine("}");
                    } break;

                    case MemberTypeEnum.Array_Of_Enum:
                    {
                        string nativeCountFieldName = member.NativeFieldName + "_count";

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

                        // NOTE(agw): get enum value
                        builder.AppendLine($"switch({nativeFieldAccessor}.ToString()) {{");
                        builder.IndentedAmount += 1;
                        foreach (var entry in member.EnumEntries)
                        {
                            builder.AppendLine($"case \"{entry.EnumLiteral.Literal}\": {{");
                            builder.IndentedAmount += 1;

                            var newCode = $"new Code<{underlyingFullName}> ()";
                            builder.AppendLine($"var __code = {newCode};");
                            builder.AppendLine($"__code.ObjectValue = {underlyingFullName}.{entry.Value};");
                            builder.AppendLine($"{listName}.Add(__code);");

                            builder.IndentedAmount -= 1;
                            builder.AppendLine($"}} break;");
                        }

                        builder.IndentedAmount -= 1;
                        builder.AppendLine("}");
                        //////

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

                            builder.AppendLine("}");
                            builder.AppendLine("");
                            builder.IndentedAmount -= 1;

                            string assignmentString = $"{firelyInstance}.{member.FirelyFieldName} = {listName};";
                            builder.AppendLine(assignmentString);
                            builder.AppendLine("");

                            builder.AppendLine("}");
                            builder.AppendLine("");
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
                            builder.AppendLine("}");
                            builder.AppendLine("");

                            string assignmentString = $"{firelyInstance}.{member.FirelyFieldName} = {listName};";
                            builder.AppendLine(assignmentString);
                            builder.AppendLine("");

                            builder.IndentedAmount -= 1;
                            builder.AppendLine("}");
                            builder.AppendLine("");
                        }
                        break;
                }
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
                builder.AppendLine($"case FHIR_Marshalling.ResourceType.{kvp.Key.Name}: return ({firelyResourceName}){deserializeName}(({kvp.Key.FullName}*)resource);");
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
            if(MainClass.nativeToFirely.TryGetValue(nativeType, out firelyResourceType) == false) {
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
                if (fhirName == "resourceType") continue;

                memberMapping.FhirName = fhirName;
                memberMapping.NativeField = field;

                var firelyProp = MainClass.Firely_GetPropertyWithFhirElementName(outputResource.GetType(), fhirName);
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
                        Type unionFieldType = unionField.FieldType;
                        if(unionFieldType.IsPointer) { unionFieldType = unionFieldType.GetElementType();}

                        if(!nativeToFirely.TryGetValue(unionFieldType, out unionFirelyType))
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

                    if (firelyPropertyType.IsGenericType && field.FieldType == typeof(String8))
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
                        if (firelyPropertyType.GetGenericArguments()[0].IsGenericType && field.FieldType == typeof(String8*))
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

                        memberMapping.NativeType = pointedToStructureType;
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

                        memberMapping.NativeType = pointedToStructureType.UnderlyingSystemType.GetElementType();
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
    }

    public class IndentedStringBuilder
    {
        public int IndentedAmount = 0;
        StringBuilder builder;
        public IndentedStringBuilder()
        {
            builder = new StringBuilder();
        }

        public string ToString()
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
