using FHIR_Marshalling;
using Hl7.Fhir.Model;
using System.Diagnostics;
using System.Reflection;

namespace MarshallingTests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void NullObjectCreation()
        {
            var bundle = new Hl7.Fhir.Model.Bundle();
            var sw = Stopwatch.StartNew();
            for(int i = 0; i < 100_000; i++)
            {
                var uri = new FhirUri();
                uri.ObjectValue = "test";
            }
            sw.Stop();
            double ticks = sw.ElapsedTicks;
            double nano = 1000000000.0 * ticks / Stopwatch.Frequency;
            double ms = nano / 1000000.0;
            Debug.WriteLine("Option 1: " + ms);


            sw = Stopwatch.StartNew();
            for(int i = 0; i < 100_000; i++)
            {
                var uri = new FhirUri("test");
            }
            sw.Stop();
            ticks = sw.ElapsedTicks;
            nano = 1000000000.0 * ticks / Stopwatch.Frequency;
            ms = nano / 1000000.0;
            Debug.WriteLine("Option 2: " + ms);
        }

        [TestMethod]
        public void TestMethod1()
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

            using(var file = new StreamWriter("Output/GeneratedMarshalling.cs"))
            {
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