using FHIR_Marshalling;

namespace ConsoleApp1
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            var d = new NativeFHIRDeserializer(1);
            string text = File.ReadAllText("C:\\Users\\awalley\\Code\\native\\test_bundles\\137555.json");
            using(var file = File.Open("C:\\Users\\awalley\\Code\\native\\test_bundles\\137555.json", FileMode.Open))
            {
                var res = d.DeserializeStream(file);
            }
        }
    }
}
