using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

// Dump the plugin-relevant API surface of a host's assemblies (docs/EMBY-COMPAT.md M2:
// "接口面以反编译/实测为准"). Pure reflection over local DLLs — no proprietary code is
// copied; the tool only prints type/member shapes.
//
// Usage: dotnet run --project tools/apidump -- <refsDir>

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: apidump <refsDir>");
    return 2;
}

var refsDir = Path.GetFullPath(args[0]);
var alc = new AssemblyLoadContext("hostrefs", isCollectible: true);
alc.Resolving += (ctx, name) =>
{
    var p = Path.Combine(refsDir, name.Name + ".dll");
    return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
};

var loaded = new List<Assembly>();
foreach (var dll in Directory.GetFiles(refsDir, "*.dll").OrderBy(f => f))
{
    try
    {
        loaded.Add(alc.LoadFromAssemblyPath(dll));
    }
    catch (Exception e)
    {
        Console.WriteLine($"# skip {Path.GetFileName(dll)}: {e.Message}");
    }
}

Type[] SafeTypes(Assembly asm)
{
    try
    {
        return asm.GetTypes();
    }
    catch (ReflectionTypeLoadException e)
    {
        return e.Types.Where(t => t != null).ToArray();
    }
}

// 1. assembly overview incl. target framework
foreach (var asm in loaded)
{
    var tf = asm.GetCustomAttributesData()
        .FirstOrDefault(a => a.AttributeType.Name == "TargetFrameworkAttribute");
    Console.WriteLine($"ASM  {asm.GetName().Name}  v{asm.GetName().Version}  tfm={(tf?.ConstructorArguments.FirstOrDefault().Value ?? "?")}");
}

static string FormatType(Type t)
{
    if (t == null)
    {
        return "?";
    }

    if (t.IsGenericType)
    {
        var head = t.Name.Split('`')[0];
        return t.Namespace + "." + head + "<" + string.Join(", ", t.GetGenericArguments().Select(FormatType)) + ">";
    }

    return t.FullName ?? t.Name;
}

// 2. keyword types: full shape
var keywords = new[]
{
    "IService", "IReturn", "Route", "TaskTriggerInfo", "IScheduledTask",
    "PluginPageInfo", "IHasWebPages", "BasePlugin", "IPluginServiceRegistrator",
    "ITaskManager", "IScheduledTaskWorker", "IServerApplicationHost",
    "IApplicationPaths", "IXmlSerializer", "IPlugin", "BasePluginConfiguration",
    "ServiceController", "IHttpListener", "RestMethod",
    "IRequiresRequest", "IRequest", "IResponse", "IHttpResultFactory", "IResultFactory",
    "TaskOptions", "ILogManager", "Authenticated", "IApplicationHost", "IHasRequestFilter",
    "TaskResult", "IStreamFactory", "ILogger", "ResourceNotFound", "HttpError", "SecurityException",
};

foreach (var asm in loaded.OrderBy(a => a.GetName().Name))
{
    foreach (var t in SafeTypes(asm).Where(t => t.IsPublic || t.IsNestedPublic))
    {
        if (!keywords.Any(k => t.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            continue;
        }

        Console.WriteLine($"\nTYPE {t.FullName}  [{asm.GetName().Name}]");
        Console.WriteLine($"  base: {t.BaseType?.FullName ?? "-"}");
        var ifaces = t.GetInterfaces().Select(i => i.Name).OrderBy(n => n).ToArray();
        if (ifaces.Length > 0)
        {
            Console.WriteLine($"  ifaces: {string.Join(", ", ifaces)}");
        }

        foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            try
            {
                Console.WriteLine($"  ctor{(c.IsPublic ? "" : "(nonpub)")}({string.Join(", ", c.GetParameters().Select(p => FormatType(p.ParameterType) + " " + p.Name))})");
            }
            catch (Exception e)
            {
                Console.WriteLine($"  ctor(?) signature unresolved: {e.GetType().Name}");
            }
        }

        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            try
            {
                Console.WriteLine($"  prop {FormatType(p.PropertyType)} {p.Name}");
            }
            catch (Exception)
            {
                Console.WriteLine($"  prop(?) {p.Name}");
            }
        }

        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName))
        {
            try
            {
                Console.WriteLine($"  meth{(m.IsPublic ? "" : "(nonpub)")} {FormatType(m.ReturnType)} {m.Name}({string.Join(", ", m.GetParameters().Select(p => FormatType(p.ParameterType) + " " + p.Name))})");
            }
            catch (Exception)
            {
                Console.WriteLine($"  meth(?) {m.Name}");
            }
        }

        // custom attribute names (route decorators etc.)
        try
        {
            var attrs = t.GetCustomAttributesData()
                .Where(a => !a.AttributeType.Name.StartsWith("CompilerGenerated"))
                .Select(a => a.AttributeType.Name + "(" + string.Join(",", a.ConstructorArguments.Select(x => x.Value)) + ")")
                .ToArray();
            if (attrs.Length > 0)
            {
                Console.WriteLine($"  attrs: {string.Join(" ", attrs)}");
            }
        }
        catch (Exception)
        {
            // attribute metadata unreadable; skip
        }
    }
}

// 3. how service methods are shaped: any public method named Get/Post on IService implementors
Console.WriteLine("\n== IService implementors (method shape) ==");
foreach (var asm in loaded)
{
    foreach (var t in SafeTypes(asm).Where(t => t.IsPublic && t.GetInterfaces().Any(i => i.Name == "IService")))
    {
        Console.WriteLine($"  {t.FullName} [{asm.GetName().Name}]");
    }
}

return 0;
