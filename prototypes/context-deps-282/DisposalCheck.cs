// PROTOTYPE for ScheduleLib #282. Throwaway.
// Does Microsoft DI dispose a value that a scoped lambda registration merely hands out?
using Microsoft.Extensions.DependencyInjection;

namespace Disposal;

public sealed class Session : IDisposable
{
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

public sealed class Holder
{
    public Session? Value { get; set; }
}

public static class DisposalCheck
{
    public static void Run(Action<string, Func<string>> tryRun)
    {
        var services = new ServiceCollection();
        services.AddScoped<Holder>();
        services.AddScoped(sp => sp.GetRequiredService<Holder>().Value!); // like Slot<T>.Value
        using var root = services.BuildServiceProvider();

        var parent = root.CreateScope();
        var session = new Session();
        parent.ServiceProvider.GetRequiredService<Holder>().Value = session;
        parent.ServiceProvider.GetRequiredService<Session>();

        using (var child = root.CreateScope())
        {
            child.ServiceProvider.GetRequiredService<Holder>().Value = session; // "inherited"
            child.ServiceProvider.GetRequiredService<Session>();
        }
        tryRun("child scope ended, parent still open", () => $"parent's session disposed: {session.Disposed}");
        parent.Dispose();
    }
}
