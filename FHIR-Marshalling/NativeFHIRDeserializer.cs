using System;
using System.Runtime.InteropServices;

namespace FHIR_Marshalling
{
    internal unsafe static class NativeMethods
    { 
        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void ND_Init();

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void ND_Cleanup();

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void ND_DeserializeFile(string file_name, ref IntPtr ptr);
    }

    public class NativeFHIRDeserializer
    {
        public NativeFHIRDeserializer()
        {
            NativeMethods.ND_Init();
        }

        ~NativeFHIRDeserializer()
        {
            NativeMethods.ND_Cleanup();
        }

        public unsafe Hl7.Fhir.Model.Resource? DeserializeFile(string fileName)
        {
            IntPtr intPtr = IntPtr.Zero;
            NativeMethods.ND_DeserializeFile(fileName, ref intPtr);
            return GeneratedMarshalling.Marshal_Resource((Resource*)intPtr);
        }
    }
}
