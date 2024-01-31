using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

/*
 *  TODO(agw):
 *  We need to recurse through Firely's classes, noting which field's are 
 *  pertinent to us (R4).
 *  Once we have these classes and fields, we need to construct and compile Get'ers and Set'ers for each.
 *  
 *  We could even match up the fields with our fields directly (maybe)
 *  If we can do that pre-compile time, it eliminated the need to search for a match at runtime.
 * 
 */

namespace FHIR_Marshalling
{
    public unsafe static class MainClass
    {
        public static void Main(string[] args)
        {
            var dirs = Directory.EnumerateFiles("Input/patient-bundles");
            /*
            dirs = new string[]
            {
                "D:/Programming Stuff/FHIR-Marshalling/FHIR-Marshalling/bin/x64/Debug/Input/patient-bundles/008a1536-cf42-6f72-657b-c14e201ee475.json"
            };
            */

            IFhirSerializationEngine Serializer = FhirSerializationEngineFactory.Ostrich(ModelInfo.ModelInspector);
            var options = new JsonSerializerOptions().ForFhir(ModelInfo.ModelInspector).Pretty();

            var nativeDeserializer = new NativeFHIRDeserializer();

            double firelyTime = 0;
            double marshallingTime = 0;
            double dllTime = 0;

            double totalGigabytes = 0;
            Stopwatch sw = new Stopwatch();

            foreach (var fileName in dirs)
            {
                Console.WriteLine(fileName);
                IntPtr intPtr = IntPtr.Zero;

                sw.Restart();
                var codeGenBundle  =nativeDeserializer.DeserializeFile(fileName);
                NativeMethods.ND_DeserializeFile(fileName, ref intPtr);
                sw.Stop();
                double ticks = sw.ElapsedTicks; 
                double nano = 1000000000.0 * ticks / Stopwatch.Frequency;
                double ms = nano / 1000000.0;
                dllTime += ms;

                /*
                using(StreamWriter writer = new StreamWriter("Output/profiles-others-native.json"))
                {
                    string str = JsonSerializer.Serialize(codeGenBundle, options);
                    writer.Write(str);
                }
                */
            }

            foreach (var fileName in dirs)
            {
                var jsonInput = File.ReadAllText(fileName);
                sw.Restart();
                var patient = Serializer.DeserializeFromJson(jsonInput) as Hl7.Fhir.Model.Bundle;
                sw.Stop();
                double ticks = sw.ElapsedTicks; 
                double nano = 1000000000.0 * ticks / Stopwatch.Frequency;
                double ms = nano / 1000000.0;
                firelyTime += ms;

                /*
                using(StreamWriter writer = new StreamWriter("Output/profiles-others-firely.json"))
                {
                    string str = JsonSerializer.Serialize(patient, options);
                    writer.Write(str);
                }
                */

                var fileInfo = new FileInfo(fileName);
                totalGigabytes += (double)fileInfo.Length / (double)(1024 * 1024 * 1024);
            }

            Console.WriteLine("Native Deserialize Time: " + dllTime);
            Console.WriteLine("Code Gen Time: " + marshallingTime);
            Console.WriteLine("Firely Time: " + firelyTime);
            Console.WriteLine("Total Gigabytes: " + totalGigabytes);

            double nativeSeconds = (dllTime) / (double)1000;
            double nativeGigabytesPerSecond = totalGigabytes / nativeSeconds;
            Console.WriteLine("Native GB/s: " + nativeGigabytesPerSecond);

            double seconds = (dllTime + marshallingTime) / (double)1000;
            double gigabytesPerSecond = totalGigabytes / seconds;
            Console.WriteLine("GB/s: " + gigabytesPerSecond);


            double FirelySeconds = firelyTime / (double)1000;
            double FirelyGigabytesPerSecond = totalGigabytes / FirelySeconds;
            Console.WriteLine("Firely GB/s: " + FirelyGigabytesPerSecond);

            Console.WriteLine("Native Speed Multiplier: " + (nativeGigabytesPerSecond / FirelyGigabytesPerSecond));
            Console.WriteLine("Marshalling Speed Multiplier: " + (gigabytesPerSecond / FirelyGigabytesPerSecond));
        }

    }
}
