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

    namespace FHIR_Marshalling
    {
        internal unsafe static class NativeDeserializerMethods
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

        public class NativeFHIRDeserializer : IDisposable
        {
            public NativeFHIRDeserializer(int num_contexts = 0)
            {
                NativeDeserializerMethods.ND_Init(num_contexts);
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
                    NativeDeserializerMethods.ND_Cleanup();
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
                byte[]? bytes = null;
                if (!stream.CanSeek)
                {
                    // NOTE(agw): We do not know the length, do buffered copy
                    using MemoryStream memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);

                    memoryStream.Write(SIMDJSON_PADDING, 0, SIMDJSON_PADDING.Length);

                    bytes = memoryStream.ToArray();
                }
                else
                {
                    // NOTE(agw): If we _do_ know the length, just copy everything directly
                    bytes = new byte[stream.Length + SIMDJSON_PADDING.Length];
                    if (stream.Length <= int.MaxValue)
                    {
                        stream.Write(bytes, 0, (int)stream.Length);
                    }
                    else
                    {
                        throw new Exception();
                    }
                }


                IntPtr intPtr = IntPtr.Zero;
                IntPtr context = (IntPtr)0;
                fixed (byte* byte_ptr = bytes)
                {
                    context = NativeDeserializerMethods.ND_DeserializeString(byte_ptr, bytes.Length, ref intPtr);
                }

                var res = GeneratedMarshalling.Marshal_Resource((Resource*)intPtr);
                NativeDeserializerMethods.ND_FreeContext(context);
                return res;
            }

            public unsafe Hl7.Fhir.Model.Resource? DeserializeFile(string fileName)
            {
                IntPtr intPtr = IntPtr.Zero;
                var context = NativeDeserializerMethods.ND_DeserializeFile(fileName, ref intPtr);
                var res = GeneratedMarshalling.Marshal_Resource((Resource*)intPtr);
                NativeDeserializerMethods.ND_FreeContext(context);

                return res;
            }

        }
    }
