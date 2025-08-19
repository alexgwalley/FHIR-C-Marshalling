# FHIR-C-Marshalling

This project is a C# wrapper to use the native FHIR deserializer from the FHIR-in-C project.

### Example usage:
```c#
using FHIR_Marshalling;
...
var deserializer = new NativeFHIRDeserializer();

// NOTE: Deserialize directly from a file
deserializer.DeserializeFile("file_name.json");

// NOTE: Deserialize from a stream
using (var stream = File.Open("file_name", FileMode.Open))
{
    deserializer.DeserializeStream(stream);
}
```

The DLL handles being called from multiple threads, so you can freely use the same deserializer in parallel loops as such:
```c#
var deserializer = new NativeFHIRDeserializer();
var files = Directory.EnumerateFiles("input");
Parallel.ForEach(files, file =>
{
    Hl7.Fhir.Model.Resource? resource = deserializer.DeserializeFile(file);
});
```

For performance reasons, do not construct a new deserializer in each thread.

### To build from scratch:
 - Build the project in Visual Studio
 - Run the BuildCodeGen Unit test in the `MarshallingTests` project to generate the C# native -> Firely SDK marshalling code (GeneratedMarshalling.cs). This will be placed `bin/.../Output/GeneratedMarshalling.cs`
 - Copy this file to the Fhir-Marshalling project under `generated/`.
 - Copy `CSGeneratedClasses.cs` and the deserialize dll from FHIR-in-C project.

### Limitations:
 - Only works on FHIR R4 JSON, no other versions supported (yet)

