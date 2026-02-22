using System.Reflection;

var asm = Assembly.LoadFrom(@"C:\Users\90lec\.nuget\packages\silk.net.core\2.22.0\lib\net6.0\Silk.NET.Core.dll");
var comPtrTypes = asm.GetTypes().Where(t => t.Name.StartsWith("ComPtr")).ToList();
foreach (var t in comPtrTypes)
{
    Console.WriteLine($"=== {t.FullName} (IsValueType={t.IsValueType}) ===");
    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType} {p.Name}"));
        Console.WriteLine($"  {m.ReturnType} {m.Name}({parms})");
    }
    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        Console.WriteLine($"  PROP: {p.PropertyType} {p.Name}");
    }
}
