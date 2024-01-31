using Hl7.Fhir.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FHIR_Marshalling 
{
    public class ManualDeserialization
    {
        public static object GetEnumValueFromString(Type enumType, string value)
        {
            if (value == null) return null;
            var entries = MainClass.GetEnumEntries(enumType);

            foreach (var entry in entries)
            {
                if (entry.EnumLiteral.Literal.Equals(value))
                {
                    return entry.Value;
                }
            }

            throw new NotImplementedException();
        }

        public static string? StringFromString8(String8 str)
        {
            if((ulong)str.size == 0) return null;
            return str.ToString();
        }

    }
}
