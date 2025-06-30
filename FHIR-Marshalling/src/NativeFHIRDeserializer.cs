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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

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
        public static extern ND_Result DeserializeFile(ND_Handle Context, string file_name);

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern ND_Result DeserializeString(ND_Handle Context, byte* bytes, Int64 length);

        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern ND_Handle CreateContext();
        [DllImport("deserialization_dll.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void FreeContext(ND_Handle Context);
#endif

#if LINUX
        [DllImport("libdeserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void Init();

        [DllImport("libdeserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void Cleanup();

        [DllImport("libdeserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern ND_Result DeserializeFile(ND_Handle Context, string file_name);

        [DllImport("libdeserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern ND_Result DeserializeString(ND_Handle Context, byte* bytes, Int64 length);

        [DllImport("libdeserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern ND_Handle CreateContext();

        [DllImport("libdeserialization_dll.so", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void FreeContext(ND_Handle Context);
#endif
    }

    public class NativeFHIRDeserializer : IDisposable
    {
        public NativeFHIRDeserializer(int num_Contexts = 0, bool throwOnErrors = false)
        {
            NativeDeserializerMethods.Init();
            _Contexts = new ConcurrentStack<ND_Handle>();
            _ThrowOnErrors = throwOnErrors;
        }

        private ConcurrentStack<ND_Handle> _Contexts;
        private bool _ThrowOnErrors = false;

        private ND_Handle GetContext()
        {
            ND_Handle result = new ND_Handle{};
            if(_Contexts.TryPop(out result) == false)
            {
                result = NativeDeserializerMethods.CreateContext();
            }
            return result;
        }

        private void ReleaseContext(ND_Handle Context)
        {
            _Contexts.Push(Context);
        }

        public static readonly byte[] SIMDJSON_PADDING = new byte[64];
        private bool disposedValue;

        private void HandleExceptions(ND_Result deserialization_result)
        {
            // ~ TODO(agw): do stuff with errors
            string error_string = deserialization_result.error_message.ToString();
            if (error_string.Length > 0)
            {
                throw new JsonException(error_string);
            }

            StringBuilder error_builder = new StringBuilder();
            unsafe
            {
                for (LogNode* log = deserialization_result.logs.first; log != null; log = log->next)
                {
                    if(log->type == LogType.Error)
                    {
                        error_builder.AppendLine(log->log_message.ToString());
                    }
                }
            }

            string log_error_string = error_builder.ToString();
            if(log_error_string.Length > 0 && _ThrowOnErrors)
            {
                throw new JsonException(log_error_string);
            }
        }

        private unsafe Hl7.Fhir.Model.Resource? DeserializeBytes(byte[] bytes)
        {
            Hl7.Fhir.Model.Resource result = null;

            // ~ Get ND Context (memory arenas, re-use simdjson parser, etc.)
            ND_Handle Context = this.GetContext();

            // ~ Deserialize
            ND_Result deserialization_result = new ND_Result();
            if (bytes.Length > 0)
            {
                fixed (byte* byte_ptr = bytes)
                {
                    deserialization_result = NativeDeserializerMethods.DeserializeString(Context, byte_ptr, bytes.Length);
                }
            }

            // ~ Marshal to Managed Memory
            if (deserialization_result.resource != IntPtr.Zero)
            {
                result = GeneratedMarshalling.Marshal_Resource((Resource*)deserialization_result.resource);
            }

            // ~ Reuse Context for later
            this.ReleaseContext(Context);

            HandleExceptions(deserialization_result);

            return result;

        }

        public unsafe Hl7.Fhir.Model.Resource? DeserializeString(string json)
        {
            Hl7.Fhir.Model.Resource? result = null;

            byte[] utf8Bytes = Encoding.UTF8.GetBytes(json);
            byte[] bytes = new byte[utf8Bytes.Length + SIMDJSON_PADDING.Length];
            Buffer.BlockCopy(utf8Bytes, 0, bytes, 0, utf8Bytes.Length);
            Buffer.BlockCopy(SIMDJSON_PADDING, 0, bytes, utf8Bytes.Length, SIMDJSON_PADDING.Length);

            result = DeserializeBytes(bytes);
            return result;
        }

        public unsafe Hl7.Fhir.Model.Resource? DeserializeStream(Stream stream)
        {
            Hl7.Fhir.Model.Resource result = null;

            // ~ Copy Entire Stream
            byte[]? bytes = null;
            {
                using MemoryStream memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);

                memoryStream.Write(SIMDJSON_PADDING, 0, SIMDJSON_PADDING.Length);

                bytes = memoryStream.ToArray();
            }

            result = DeserializeBytes(bytes);
            return result;
        }

        public Hl7.Fhir.Model.Resource? DeserializeFile(string file_name)
        {
            Hl7.Fhir.Model.Resource? result = null;

            ND_Handle Context = this.GetContext();
            ND_Result deserialization_result = NativeDeserializerMethods.DeserializeFile(Context, file_name);

            this.ReleaseContext(Context);

            HandleExceptions(deserialization_result);

            return result;
        }

        ////////////////////////////////////////////////////////////
        // ~ IDisposable Implementation
        ////////////////////////////////////////////////////////////

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // NOTE(agw): Dispose of all managed state (managed objects)
                }

                // NOTE(agw): Dispose of all un-managed state
                foreach (var Context in _Contexts)
                {
                    NativeDeserializerMethods.FreeContext(Context);
                }
                _Contexts.Clear();

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

    }
}
