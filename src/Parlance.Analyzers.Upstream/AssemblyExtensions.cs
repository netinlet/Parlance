using System.Reflection;

namespace Parlance.Analyzers.Upstream;

internal static class AssemblyExtensions
{
    /// <summary>
    /// Discovers and instantiates all concrete types assignable to <typeparamref name="T"/>
    /// in the given assembly. Handles <see cref="ReflectionTypeLoadException"/> gracefully.
    /// </summary>
    public static List<T> DiscoverInstances<T>(this Assembly assembly) where T : class
    {
        var results = new List<T>();
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }
        catch
        {
            return results;
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || !typeof(T).IsAssignableFrom(type))
                continue;

            try
            {
                if (Activator.CreateInstance(type) is T instance)
                    results.Add(instance);
            }
            catch
            {
                // Skip types that can't be instantiated
            }
        }

        return results;
    }
}
