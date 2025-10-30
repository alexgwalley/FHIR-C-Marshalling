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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace MarshallingTests
{
    [TestClass]
    public class DeserializeTests
    {
        [TestMethod]
        public void Test()
        {
            //string fileName = "/mnt/c/Users/awalley/Code/native/test_bundles/patient.2024.acp.0.95000.json";
            string fileName = "C:/Users/awalley/Code/native/test_bundles/patient.2024.acp.0.95000.json";
            Console.WriteLine("Hello! Loading file: " + fileName);
            var d = new NativeFHIRDeserializer();
            using (var file = File.Open(fileName, FileMode.Open))
            {
                Console.WriteLine("Loaded File" + fileName);
                var res = d.DeserializeStream(file);
                Debug.WriteLine(res.Id);
                int a = 0;
            }
        }

        [TestMethod]
        public void InvalidJson()
        {
            string json = "{\"resourceType\": \"Bundle\"";
            var deserializer = new NativeFHIRDeserializer();
            bool hadException = false;
            try
            {
                var resource = deserializer.DeserializeString(json);
            }
            catch (JsonException ex)
            {
                hadException = true;
            }
            Assert.IsTrue(hadException);
        }


        [TestMethod]
        public void FilterTest()
        {
            string json = """
{
	"resourceType": "Bundle",
	"entry": [
			{
				"resource": {
					"resourceType": "Condition",
					"id": "condition-0",
					"code": {
						"coding": [{
								"code": "G9473",
								"system": "http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets"
							}]
					},
                    "encounter": {
                        "reference": "Encounter/random-encounter-1"
                    },
                    "subject": {
                        "reference": "Patient/patient-0"
                    }
				}
			},
			{
				"resource": {
					"resourceType": "Encounter",
					"id": "random-encounter-1"
				}
			},
			{
				"resource": {
					"resourceType": "Patient",
					"id": "patient-0"
				}
			},
			{
				"resource": {
					"resourceType": "Coverage",
					"id": "coverage-0"
				}
			},
			{
				"resource": {
					"resourceType": "Encounter",
					"id": "encounter-1",
                    "subject": {
                        "reference": "Condition/condition-0"
                    }
				}
			},
			{
				"resource": {
					"resourceType": "Encounter",
					"id": "encounter-0",
					"class": {
						"code": "bad_code",
						"system": "http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets"
					}
				}
			}
	]
}
""";
            //string vsdFolder = "C:/Users/awalley/Code/Ncqa.IMAS/Ncqa.IMAS.MeasureCompiler/TerminologyServer/ValueSets/2025-03-31";
            string vsdFolder = "C:/Users/awalley/Downloads/MY25_AllCodes.txt";
            var deserializer = new NativeFHIRDeserializer(valueSetDictionaryFolder: vsdFolder);
            bool hadException = false;
            try
            {
                var resource = deserializer.DeserializeString(json, filterOutBadCodes: true) as Hl7.Fhir.Model.Bundle;
                Assert.IsTrue(resource.Entry.Count == 5);
            }
            catch (JsonException ex)
            {
                hadException = true;
            }
        }


        [TestMethod]
        public void FilterTest2()
        {
            string json = File.ReadAllText("C:/Users/awalley/Downloads/patient.2024.aab.0.95524.json");
            //string vsdFolder = "C:/Users/awalley/Code/Ncqa.IMAS/Ncqa.IMAS.MeasureCompiler/TerminologyServer/ValueSets/2025-03-31";
            string vsdFolder = "C:/Users/awalley/Downloads/MY24_AllCodes.txt";
            var deserializer = new NativeFHIRDeserializer(valueSetDictionaryFolder: vsdFolder);
            bool hadException = false;
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                for (int i = 0; i < 1000; i += 1)
                {
                    var resource = deserializer.DeserializeString(json, filterOutBadCodes: true) as Hl7.Fhir.Model.Bundle;
                }
                int a = 0;
            }
            catch (JsonException ex)
            {
                hadException = true;
            }
            sw.Stop();
            Debug.WriteLine("Total Elapsed: " + sw.Elapsed.TotalMilliseconds);
           
        }
    }
}