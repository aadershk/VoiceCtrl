using System.Collections;
using System.Reflection;
using VoiceCtrl.Core.Bridge;

namespace VoiceCtrl.Tools.BridgeTypegen;

public sealed record CommandContract(string Name, Type Args, Type? Result);

public sealed record EventContract(string Name, Type Payload);

/// <summary>
/// Every bridge contract in an assembly: the <c>[BridgeCommand]</c> and <c>[BridgeEvent]</c>
/// records, plus every record and enum they reach through their properties.
/// </summary>
public sealed class ContractCatalog
{
    private ContractCatalog(IReadOnlyList<CommandContract> commands, IReadOnlyList<EventContract> events, IReadOnlyList<Type> allTypes)
    {
        Commands = commands;
        Events = events;
        AllTypes = allTypes;
    }

    /// <summary>Sorted by wire name.</summary>
    public IReadOnlyList<CommandContract> Commands { get; }

    /// <summary>Sorted by wire name.</summary>
    public IReadOnlyList<EventContract> Events { get; }

    /// <summary>Every record and enum that crosses the bridge, sorted by name.</summary>
    public IReadOnlyList<Type> AllTypes { get; }

    public static ContractCatalog Read(Assembly assembly)
    {
        var commands = new List<CommandContract>();
        var events = new List<EventContract>();

        foreach (Type type in assembly.GetExportedTypes())
        {
            if (type.GetCustomAttribute<BridgeCommandAttribute>() is { } command)
            {
                commands.Add(new CommandContract(command.Name, type, command.Result));
            }

            if (type.GetCustomAttribute<BridgeEventAttribute>() is { } bridgeEvent)
            {
                events.Add(new EventContract(bridgeEvent.Name, type));
            }
        }

        var reached = new HashSet<Type>();
        foreach (CommandContract command in commands)
        {
            Reach(command.Args, reached);
            if (command.Result is not null)
            {
                Reach(command.Result, reached);
            }
        }

        foreach (EventContract bridgeEvent in events)
        {
            Reach(bridgeEvent.Payload, reached);
        }

        return new ContractCatalog(
            [.. commands.OrderBy(c => c.Name, StringComparer.Ordinal)],
            [.. events.OrderBy(e => e.Name, StringComparer.Ordinal)],
            [.. reached.OrderBy(t => t.Name, StringComparer.Ordinal)]);
    }

    /// <summary>The properties that go on the wire, in declaration order.</summary>
    public static IEnumerable<PropertyInfo> WireProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is not null && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.MetadataToken);

    /// <summary>The element type of a list-like property type, or null when it isn't one.</summary>
    public static Type? ElementType(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType
            ? type.GetGenericArguments()[0]
            : null;
    }

    private static void Reach(Type type, HashSet<Type> reached)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (ElementType(type) is { } element)
        {
            Reach(element, reached);
            return;
        }

        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(Guid)
            || !reached.Add(type) || type.IsEnum)
        {
            return;
        }

        foreach (PropertyInfo property in WireProperties(type))
        {
            Reach(property.PropertyType, reached);
        }
    }
}
