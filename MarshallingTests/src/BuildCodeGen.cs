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

using FHIR_Marshalling;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MarshallingTests
{
    [TestClass]
    public class BuildCodeGen
    {
        const string MIT_LICENSE = @"
MIT License

Copyright (c) 2024 Alex Walley

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
";
        [TestMethod]
        public void RunBuildCodeGen()
        {
            var funcs = new List<string>();
            var keys = CodeGen.NativeToFirely.Keys;

            foreach(var key in keys)
            {
                if (key == typeof(FHIR_Marshalling.Resource)) continue;

                var mappingInfo = CodeGen.GetFirelyMappingInfo(key);
                var liquidator = CodeGen.GetFhirLiquidator(key, mappingInfo);
                funcs.Add(liquidator);
            }

            funcs.Add(CodeGen.GetDeserializeResource(CodeGen.NativeToFirely));

            var dir = Directory.GetCurrentDirectory();
            var outputDir = Path.Combine(dir, "Output");
            Directory.CreateDirectory(outputDir);

            using(var file = new StreamWriter("Output/GeneratedMarshalling.cs"))
            {
                file.WriteLine(MIT_LICENSE);
                file.WriteLine("using Hl7.Fhir.Model;");
                file.WriteLine("using FHIR_Marshalling;");
                file.WriteLine("using System;");
                file.WriteLine("using System.Collections.Generic;");
                file.WriteLine("using System.Runtime.InteropServices;");
                file.WriteLine("using System.Runtime.Serialization;");

                file.WriteLine("namespace FHIR_Marshalling");
                file.WriteLine("{");

                file.WriteLine("public unsafe class GeneratedMarshalling");
                file.WriteLine("{");

                foreach (var func in funcs)
                {
                    file.Write(func);
                    file.WriteLine("");
                    file.WriteLine("");
                    file.WriteLine("");
                }

                file.WriteLine("}");
                file.WriteLine("}");
            }
        }
    }
}