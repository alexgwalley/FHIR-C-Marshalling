using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

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

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void ND_DeserializeString(byte* bytes, Int64 length, ref IntPtr ptr);
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

        public static readonly int SIMDJSON_PADDING = 64;
        public unsafe Hl7.Fhir.Model.Resource? DeserializeStream(Stream stream)
        {
            stream.Seek(0, SeekOrigin.End);
            var streamLen = stream.Position;
            stream.Seek(0, SeekOrigin.Begin);

            var memoryStream = new MemoryStream((int)streamLen + SIMDJSON_PADDING);
            stream.CopyTo(memoryStream);

            byte[] buffer = new byte[SIMDJSON_PADDING];
            memoryStream.Write(buffer, 0, buffer.Length);

            byte[] bytes = memoryStream.ToArray();

            IntPtr intPtr = IntPtr.Zero;
            fixed (byte* byte_ptr = bytes)
            {
                NativeMethods.ND_DeserializeString(byte_ptr, bytes.Length, ref intPtr);
            }

            Hl7.Fhir.Model.Resource res = GeneratedMarshalling.Marshal_Resource((Resource*)intPtr);
            memoryStream.Close();
            return res;
        }

        public unsafe Hl7.Fhir.Model.Resource? DeserializeFile(string fileName)
        {
            IntPtr intPtr = IntPtr.Zero;
            NativeMethods.ND_DeserializeFile(fileName, ref intPtr);
            return GeneratedMarshalling.Marshal_Resource((Resource*)intPtr);
        }
    }
}
