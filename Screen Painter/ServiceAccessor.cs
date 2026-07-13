using System;

namespace Screen_Painter;

public static class ServiceAccessor
{
    private static IServiceProvider? _serviceProvider;

    public static void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public static T? GetService<T>() where T : class
    {
        return _serviceProvider?.GetService(typeof(T)) as T;
    }

    public static object? GetService(Type type)
    {
        return _serviceProvider?.GetService(type);
    }
}
