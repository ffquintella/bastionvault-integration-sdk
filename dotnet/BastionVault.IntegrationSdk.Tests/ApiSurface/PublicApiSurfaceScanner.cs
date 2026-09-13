using System.Reflection;
using System.Text;

namespace BastionVault.IntegrationSdk.Tests.ApiSurface;

/// <summary>
/// Mechanically captures the public API surface of an assembly by reflection (D-M1b-19).
/// Replaces reliance on <c>Microsoft.CodeAnalysis.PublicApiAnalyzers</c> (RS0016/RS0017), which is
/// silently inert under this project's <c>AnalysisLevel=latest-all</c> (confirmed: `NetAnalyzers.props`
/// reassigns <c>$(CodeAnalysisRuleIds)</c>, dropping the RS00xx set the analyzer relies on). This is
/// the same category of check Rust (<c>cargo public-api</c>) and Python
/// (<c>tests/test_api_surface.py</c>) already run for CNF-027; .NET now has one too.
/// </summary>
internal static class PublicApiSurfaceScanner
{
    /// <summary>Returns one line per public type and public member, sorted for a stable diff.</summary>
    public static IReadOnlyList<string> Capture(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        List<string> lines = new();

        foreach (Type type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            string owner = type.FullName ?? type.Name;
            lines.Add($"{owner} : type {Describe(type)}");

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (ConstructorInfo ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).OrderBy(FormatMethod, StringComparer.Ordinal))
            {
                lines.Add($"{owner} : ctor {FormatMethod(ctor)}");
            }

            foreach (MethodInfo method in type.GetMethods(flags)
                .Where(m => !m.IsSpecialName || m.Name.StartsWith("op_", StringComparison.Ordinal))
                .OrderBy(FormatMethod, StringComparer.Ordinal))
            {
                lines.Add($"{owner} : method {FormatMethod(method)}");
            }

            foreach (PropertyInfo property in type.GetProperties(flags).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                lines.Add($"{owner} : property {FormatProperty(property)}");
            }

            foreach (FieldInfo field in type.GetFields(flags).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                lines.Add($"{owner} : field {FormatField(field)}");
            }

            foreach (EventInfo evt in type.GetEvents(flags).OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                lines.Add($"{owner} : event {evt.EventHandlerType?.FullName} {evt.Name}");
            }
        }

        return lines;
    }

    private static string Describe(Type type)
    {
        string kind = type switch
        {
            _ when type.IsInterface => "interface",
            _ when type.IsEnum => "enum",
            _ when type.IsValueType => "struct",
            _ when type.IsAbstract && type.IsSealed => "static class",
            _ when type.IsAbstract => "abstract class",
            _ when type.IsSealed => "sealed class",
            _ => "class",
        };
        return $"{type.FullName} : {kind}";
    }

    private static string FormatMethod(MethodBase method)
    {
        string parameters = string.Join(", ", method.GetParameters().Select(FormatParameter));
        string returnType = method is MethodInfo info ? TypeName(info.ReturnType) : "void";
        string generic = method.IsGenericMethodDefinition
            ? "<" + string.Join(",", method.GetGenericArguments().Select(TypeName)) + ">"
            : string.Empty;
        return $"{method.Name}{generic}({parameters}) -> {returnType}";
    }

    private static string FormatParameter(ParameterInfo parameter)
    {
        StringBuilder builder = new();
        if (parameter.IsOptional)
        {
            builder.Append("[opt] ");
        }

        builder.Append(TypeName(parameter.ParameterType)).Append(' ').Append(parameter.Name);
        return builder.ToString();
    }

    private static string FormatProperty(PropertyInfo property)
    {
        string access = (property.CanRead ? "get" : string.Empty)
            + (property.CanRead && property.CanWrite ? "/" : string.Empty)
            + (property.CanWrite ? "set" : string.Empty);
        return $"{property.Name} : {TypeName(property.PropertyType)} {{{access}}}";
    }

    private static string FormatField(FieldInfo field)
    {
        string modifier = field.IsLiteral ? "const" : field.IsInitOnly ? "readonly" : "field";
        return $"{modifier} {field.Name} : {TypeName(field.FieldType)}";
    }

    private static string TypeName(Type type)
    {
        if (type.IsGenericType)
        {
            string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
            int backtick = name.IndexOf('`', StringComparison.Ordinal);
            if (backtick >= 0)
            {
                name = name[..backtick];
            }

            string arguments = string.Join(",", type.GetGenericArguments().Select(TypeName));
            return $"{name}<{arguments}>";
        }

        return type.FullName ?? type.Name;
    }
}
