# FHIR-C-Marshalling

This project is a C# wrapper to use the native FHIR deserializer from the FHIR-in-C project.

To build from scratch:
 - Build the project in Visual Studio
 - Run the BuildCodeGen Unit test to generate the C# native -> Firely SDK marshalling code (GeneratedMarshalling.cs). This will be placed `bin/.../Output/GeneratedMarshalling.cs`
 - Now you can call `NativeFHIRDeserializer` functions (which use code in `GeneratedMarshalling`)
