# FHIR-C-Marshalling

This project is a C# wrapper to use the native FHIR deserializer from the FHIR-in-C project.
It calls the DLL to deserialize FHIR JSON then marshalls the deserialized Native FHIR C structs into Firely SDK C# classes for regular use.

To build from scratch:
 - Build the project in Visual Studio, then run the BuildCodeGen Unit test to generate the C# native -> Firely SDK marshalling code (GeneratedMarshalling.cs). This will be placed in the bin folder/Output
