using System.Reflection;
var asm = typeof(AGUI.Abstractions.BaseEvent).Assembly;
foreach (var n in new[]{"AGUIContent","AGUITextInputContent","AGUIInputContent","AGUIImageInputContent","AGUIInputContentUrlSource","AGUIJsonSerializerContext","TokenUsage","AGUIInterrupt"})
{
  var t = asm.GetExportedTypes().First(x=>x.Name==n);
  Console.WriteLine($"{t.Name} : {t.BaseType?.Name}");
  foreach (var m in t.GetMembers(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly))
    if (m is PropertyInfo p) Console.WriteLine($"   prop {p.PropertyType.Name} {p.Name}");
    else if (m is MethodInfo mi && !mi.IsSpecialName) Console.WriteLine($"   {(mi.IsStatic?"static ":"")}{mi.ReturnType.Name} {mi.Name}({string.Join(",",mi.GetParameters().Select(x=>x.ParameterType.Name))})");
}
var c = new AGUI.Abstractions.AGUIContent("hi"); Console.WriteLine($"ToString='{c}' IsText={c.IsText} Value={c.Value}");
var ctx = asm.GetType("AGUI.Abstractions.AGUIJsonSerializerContext")!;
Console.WriteLine(string.Join(", ", ctx.GetProperties().Select(p=>p.Name).Where(n=>n.Contains("Message")||n.Contains("Event")||n.Contains("RunAgentInput")||n.Contains("JsonElement")).Take(40)));
