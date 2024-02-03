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
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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
            var dirs = Directory.EnumerateFiles("D:/Programming Stuff/FHIR-Marshalling/FHIR-Marshalling/Input/patient-bundles");
            bool doSingle = false;
            if(doSingle)
            {
                dirs = new string[]
                {
                    "D:/Programming Stuff/FHIR-Marshalling/FHIR-Marshalling/Input/patient-bundles/0a95ab64-050d-590b-4310-a084df00ac88.json"
                };
            }

            IFhirSerializationEngine Serializer = FhirSerializationEngineFactory.Ostrich(ModelInfo.ModelInspector);
            var options = new JsonSerializerOptions().ForFhir(ModelInfo.ModelInspector).Pretty();

            var nativeDeserializer = new NativeFHIRDeserializer(10);

            double firelyTime = 0;
            double marshallingTime = 0;
            double dllTime = 0;
            double onlyDeserializeTime = 0;
            double copyFileTime = 0;

            double totalGigabytes = 0;
            Stopwatch sw = new Stopwatch();

            sw.Start();
            foreach (var d in dirs)
            //Parallel.ForEach(dirs, d =>
            {
                Hl7.Fhir.Model.Bundle bundle = new Hl7.Fhir.Model.Bundle();
                var stream = File.Open(d, FileMode.Open);
                bundle = (Hl7.Fhir.Model.Bundle)nativeDeserializer.DeserializeStream(stream);
                if (doSingle)
                {
                    using (StreamWriter writer = new StreamWriter("D:/Programming Stuff/FHIR-C-Marshalling/FHIR-Marshalling/Output/profiles-others-native.json"))
                    {
                        string str = JsonSerializer.Serialize(bundle, options);
                        writer.Write(str);
                    }
                }
           // });
            }
            sw.Stop();
            double ticks;
            double nano;
            double ms;
            ticks = sw.ElapsedTicks; 
            nano = 1000000000.0 * ticks / Stopwatch.Frequency;
            ms = nano / 1000000.0;
            dllTime += ms;

            /*
            foreach (var fileName in dirs)
            {

                Console.WriteLine(fileName);
                double ticks;
                double nano;
                double ms;

                sw.Restart();
                var fileStream = File.Open(fileName, FileMode.Open);
                var codeGenBundle = nativeDeserializer.DeserializeStream(fileStream);
                fileStream.Close();
                sw.Stop();
                ticks = sw.ElapsedTicks; 
                nano = 1000000000.0 * ticks / Stopwatch.Frequency;
                ms = nano / 1000000.0;
                dllTime += ms;

                using(StreamWriter writer = new StreamWriter("Output/profiles-others-native.json"))
                {
                    string str = JsonSerializer.Serialize(codeGenBundle, options);
                    writer.Write(str);
                }
            }
            */

            /*
                sw.Restart();
                Parallel.ForEach(dirs, d =>
                {
                    var jsonInput = File.ReadAllText(d);
                    var patient = Serializer.DeserializeFromJson(jsonInput) as Hl7.Fhir.Model.Bundle;
                    if (doSingle)
                    {
                        using (StreamWriter writer = new StreamWriter("D:/Programming Stuff/FHIR-C-Marshalling/FHIR-Marshalling/Output/profiles-others-firely.json"))
                        {
                            string str = JsonSerializer.Serialize(patient, options);
                            writer.Write(str);
                        }
                    }
                });
                sw.Stop();
            */

                ticks = sw.ElapsedTicks; 
                nano = 1000000000.0 * ticks / Stopwatch.Frequency;
                ms = nano / 1000000.0;
                firelyTime += ms;

                /*
                using(StreamWriter writer = new StreamWriter("Output/profiles-others-firely.json"))
                {
                    string str = JsonSerializer.Serialize(patient, options);
                    writer.Write(str);
                }
                */

            foreach(var d in dirs)
            {
                var fileInfo = new FileInfo(d);
                totalGigabytes += (double)fileInfo.Length / (double)(1024 * 1024 * 1024);
            }

            Console.WriteLine("Native Deserialize Time: " + dllTime);
            Console.WriteLine("Code Gen Time: " + marshallingTime);
            Console.WriteLine("Copy File Time: " + copyFileTime);
            Console.WriteLine("Firely Time: " + firelyTime);
            Console.WriteLine("Total Gigabytes: " + totalGigabytes);

            double nativeSeconds = (onlyDeserializeTime) / (double)1000;
            double nativeGigabytesPerSecond = totalGigabytes / nativeSeconds;
            Console.WriteLine("Native GB/s: " + nativeGigabytesPerSecond);

            double copyFileSeconds = (copyFileTime) / (double)1000;
            double copyGigabytesPerSecond = totalGigabytes / copyFileSeconds;
            Console.WriteLine("Copy File GB/s: " + copyGigabytesPerSecond);

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
