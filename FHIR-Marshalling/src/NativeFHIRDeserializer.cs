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

using Hl7.Fhir.Specification;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FHIR_Marshalling
{
    internal unsafe static class NativeDeserializerMethods
    {
#if WINDOWS
        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void Init();

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void Cleanup();

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern ND_Result DeserializeFile(ND_Handle context, string file_name);

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern ND_Result DeserializeString(ND_Handle context, byte* bytes, Int64 length);

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern ND_Handle CreateContext();
        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void FreeContext(ND_Handle context);
#endif

#if LINUX
        [DllImport("deserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void Init();

        [DllImport("deserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void Cleanup();

        [DllImport("deserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern ND_Result DeserializeFile(ND_Handle context, string file_name);

        [DllImport("deserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern ND_Result DeserializeString(ND_Handle context, byte* bytes, Int64 length);

        [DllImport("deserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern ND_Handle CreateContext();

        [DllImport("deserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void FreeContext(ND_Handle context);
#endif
    }

    public class NativeFHIRDeserializer : IDisposable
    {
        public NativeFHIRDeserializer(int num_contexts = 0)
        {
            NativeDeserializerMethods.Init();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // NOTE(agw): Dispose of all managed state (managed objects)
                }

                // NOTE(agw): Dispose of all un-managed state
                NativeDeserializerMethods.Cleanup();
                disposedValue = true;
            }
        }

        ~NativeFHIRDeserializer()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public static readonly byte[] SIMDJSON_PADDING = new byte[64];
        private bool disposedValue;

        public unsafe Hl7.Fhir.Model.Resource? DeserializeStream(Stream stream)
        {
            Hl7.Fhir.Model.Resource result = null;

            byte[]? bytes = null;
            {
                using MemoryStream memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);

                memoryStream.Write(SIMDJSON_PADDING, 0, SIMDJSON_PADDING.Length);

                bytes = memoryStream.ToArray();
            }


            // TODO(agw): re-use contexts
            ND_Handle context = NativeDeserializerMethods.CreateContext();

            ND_Result deserialization_result = new ND_Result();
            fixed (byte* byte_ptr = bytes)
            {
                deserialization_result = NativeDeserializerMethods.DeserializeString(context, byte_ptr, bytes.Length);
            }


            string error_string = deserialization_result.error_message.ToString();
            if (error_string.Length > 0)
            {
                /*
                for (LogNode* log = result.logs.first; log != null; log = log->next)
                {
                    if (log->type == LogType.Error)
                    {
                        log->log_message.ToString();
                    }
                }
                */
                //throw new Exception(str);
            }
            if(deserialization_result.resource != IntPtr.Zero)
            {
                result = GeneratedMarshalling.Marshal_Resource((Resource*)deserialization_result.resource);
            }

            NativeDeserializerMethods.FreeContext(context);
            return result;
        }

        public unsafe Hl7.Fhir.Model.Resource? DeserializeFile(string fileName)
        {
            // now we can create / destroy from C#
            ND_Handle context = NativeDeserializerMethods.CreateContext();

            ND_Result result = NativeDeserializerMethods.DeserializeFile(context, fileName);

            var res = GeneratedMarshalling.Marshal_Resource((Resource*)result.resource);

            NativeDeserializerMethods.FreeContext(context);

            return res;
        }

    }
}
