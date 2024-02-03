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
        public static extern void ND_Init(Int32 num_contexts);

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void ND_Cleanup();

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern IntPtr ND_DeserializeFile(string file_name, ref IntPtr ptr);

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern IntPtr ND_DeserializeString(byte* bytes, Int64 length, ref IntPtr ptr);

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void ND_FreeContext(IntPtr context);
    }

    public class NativeFHIRDeserializer
    {
        public NativeFHIRDeserializer(int num_contexts = 0)
        {
            NativeMethods.ND_Init(num_contexts);
        }

        ~NativeFHIRDeserializer()
        {
            NativeMethods.ND_Cleanup();
        }

        public static readonly int SIMDJSON_PADDING = 64;
        public unsafe Hl7.Fhir.Model.Resource? DeserializeStream(Stream stream)
        {
            // TODO(agw): may not support seek stream.CanSeek
            /*
            stream.Seek(0, SeekOrigin.End);
            var streamLen = stream.Position;
            stream.Seek(0, SeekOrigin.Begin);
            */

            var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            var streamLen = stream.Position;

            byte[] buffer = new byte[SIMDJSON_PADDING];
            memoryStream.Write(buffer, 0, buffer.Length);

            byte[] bytes = memoryStream.ToArray();

            IntPtr intPtr = IntPtr.Zero;
            IntPtr context = (IntPtr)0;
            fixed (byte* byte_ptr = bytes)
            {
                context = NativeMethods.ND_DeserializeString(byte_ptr, bytes.Length, ref intPtr);
            }

            Hl7.Fhir.Model.Resource res = GeneratedMarshalling.Marshal_Resource((Resource*)intPtr);
            NativeMethods.ND_FreeContext(context);
            memoryStream.Close();
            return res;
        }

        public unsafe Hl7.Fhir.Model.Resource? DeserializeFile(string fileName)
        {
            IntPtr intPtr = IntPtr.Zero;
            var context = NativeMethods.ND_DeserializeFile(fileName, ref intPtr);
            var res = GeneratedMarshalling.Marshal_Resource((Resource*)intPtr);
            NativeMethods.ND_FreeContext(context);


            return res;
        }
    }
}
