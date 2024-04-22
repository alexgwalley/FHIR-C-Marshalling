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

using Hl7.Fhir.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FHIR_Marshalling
{

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NullableString8
    {
        [FieldOffset(0)] public readonly byte* str;
        [FieldOffset(8)] public readonly UIntPtr size;
        [FieldOffset(16)] public readonly Int32 hasValue;

        public string? ToString()
        {
            if (hasValue == 0) return null;
            return Encoding.UTF8.GetString(str, (int)size);
        }
        public decimal? DecimalValue()
        {
            if ((int)size == 0) return null;

            // TODO(agw): add error handling
            return decimal.Parse(ToString());
        }

        public FhirDecimal?  ToFhirDecimal()
        {
            if((int)size == 0) return null;
            return new FhirDecimal(decimal.Parse(ToString()));
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct String16
    {
        [FieldOffset(0)] public readonly char* str;
        [FieldOffset(8)] public readonly UIntPtr size;

        public string? ToString()
        {
            if ((int)size == 0) return null;

            return new string(str, 0, (int)size);
        }

    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NullableInt32
    {
        public readonly Int32 HasValue;
        public readonly Int32 Value;
        public int? GetValue()
        {
            if(HasValue == 0) return null;
            return Value;
        }

        public Integer? ToFhirInteger()
        {
            if (HasValue == 0) return null;
            return new Integer(Value);
        }
        public UnsignedInt? ToFhirUnsignedInt()
        {
            if (HasValue == 0) return null;
            return new UnsignedInt(Value);
        }

        public PositiveInt? ToFhirPositiveInt()
        {
            if (HasValue == 0) return null;
            return new PositiveInt(Value);
        }

    };

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NullableInt64
    {
        public readonly Int32 HasValue;
        public readonly Int64 Value;
        public long? GetValue()
        {
            if(HasValue == 0) return null;
            return Value;
        }

        public Integer64? ToFhirInteger64()
        {
            if (HasValue == 0) return null;
            return new Integer64(Value);
        }

    };


    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NullableDouble
    {
        public readonly Int32 HasValue;
        public readonly double Value;

        public decimal? GetValue()
        {
            if(HasValue == 0) return null;
            return Convert.ToDecimal(Value);
        }

    };


    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NullableBoolean
    {
        public readonly Int32 HasValue;
        public readonly Int32 Value;

        public bool? GetValue()
        {
            if(HasValue == 0) return null;
            return Value == 1;
        }

        public FhirBoolean? ToFhirBoolean()
        {
            if (HasValue == 0) return null;
            return new FhirBoolean(Value == 1);

        }

    };


    [StructLayout(LayoutKind.Sequential, Size = 16)]
    public struct ISO8601_Time
    {
        public byte month;
        public byte day;
        public byte hour;
        // 'T'
        public byte minute;
        public byte second;
        // 'Z' | '+' | '-'
        public byte timezone_char;
        public byte timezone_hour;
        public byte timezone_minute;

        public ushort year;
        public Precision precision;
        public Precision min_precision;
        public UInt32 millisecond;

        public enum Precision
        {
            Unknown,
            Year,
            Month,
            Day,
            Hour,
            Minute,
            Second,
            Millisecond,
            TimezoneMinute,
            TimezoneSecond,
        };

        public DateTimeOffset? ToDateTimeOffset()
        {
            if (year == 0) return null;
            TimeSpan timespan = TimeSpan.Zero;
            if (timezone_char != 'Z' && timezone_char != 0)
            {
                timespan = new TimeSpan(timezone_hour * (timezone_char == '-' ? -1 : 1), timezone_minute, 0);
            }

            if (precision >= Precision.Millisecond)
            {
                if (millisecond > 1000)
                {
                    string timezone = "";
                    if (timezone_char > 0)
                    { 
                        timezone = timezone_char == 'Z' ? "Z" : $"{(timezone_char == '-' ? "-" : "+")}{timezone_hour:D2}:{timezone_minute:D2}";
                    }
                    string str = $"{year}-{month}-{day}T{hour}:{minute}:{second}.{millisecond}{timezone}";
                    DateTime dt = DateTime.Parse(str);
                    return dt;
                }
                else
                {
                    return new DateTimeOffset(year, month, day, hour, minute, second, (int)millisecond, timespan);
                }
            }
            else if (precision == Precision.Second)
            {
                return new DateTimeOffset(year, month, day, hour, minute, second, timespan);
            }
            else if(precision == Precision.Day)
            {
                return new DateTimeOffset(year, month, day, 0, 0, 0, timespan);
            }

            return null;
        }

        public Instant? ToFhirInstant()
        {
            if (year == 0) return null;
            if(precision != 0)
            {
                return new Instant(ToDateTimeOffset());
            }
            return null;
        }

        public Date? ToFhirDate()
        {
            if (year == 0) return null;
            if(precision == Precision.Day)
            {
                return new Date(year, month, day);
            }
            else if (precision == Precision.Month)
            {
                return new Date(year, month);
            }
            else if (precision == Precision.Year)
            {
                return new Date(year);
            }

            return null;
        }

        public Time? ToFhirTime()
        {
            if(precision >= Precision.Hour)
            {
                new Time(hour, minute, second);
            }

            return null;
        }

        public FhirDateTime? ToFhirDateTime()
        {
            if (year == 0) return null;
            if(precision >= Precision.Second)
            {
                TimeSpan timespan = TimeSpan.Zero;
                if (timezone_char != 'Z')
                {
                    timespan = new TimeSpan(timezone_hour * (timezone_char == '-' ? -1 : 1), timezone_minute, 0);
                }

                return new FhirDateTime(year, month, day, hour, minute, second, timespan);
            }
            else if(precision == Precision.Day)
            {
                return new FhirDateTime(year, month, day);
            }
            else if (precision == Precision.Month)
            {
                return new FhirDateTime(year, month);
            }
            else if (precision == Precision.Year)
            {
                return new FhirDateTime(year);
            }

            return null;
        }

    };
    public enum Cardinality
    {
        Unknown,
        ZeroToZero,
        ZeroToOne,
        ZeroToInf,
        OneToOne,
        OneToInf,
    };

    public enum ClassMemberType
    {
        Unknown,
        Single,
        Enum,
        Union
    }

    public enum ValueType
    {
        Unknown
    }
    
    public class ValueTypeAndName
    {
        public ValueType type;
        public string name;
    };


    [System.AttributeUsage(System.AttributeTargets.Field)
    ]
    public class FhirNameAttribute : System.Attribute
    {
        public string FhirName;

        public FhirNameAttribute(string fhir_name)
        {
            FhirName = fhir_name;
        }
    }


    [System.AttributeUsage(System.AttributeTargets.Field)
    ]
    public class NativeFhirTypeAttribute : System.Attribute
    {
        public Type FirelyType;

        public NativeFhirTypeAttribute(Type fhirType)
        {
            FirelyType = fhirType;
        }
    }

}
