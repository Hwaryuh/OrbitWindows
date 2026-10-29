namespace Orbit.Core.Actions;

/// <summary>
/// Runs actions (Orbit's <c>OrbitAction.perform()</c>). Each feature registers the actions it
/// implements as it starts; an action nobody registered is reported through <see cref="Unavailable"/>,
/// so the ring and the launcher can show it and say it isn't ready yet. Used on the UI thread.
/// </summary>
public sealed class ActionRunner
{
    private readonly Dictionary<OrbitAction, Action> _handlers = [];

    /// <summary>An action without a handler was asked for.</summary>
    public event Action<OrbitAction>? Unavailable;

    public void Register(OrbitAction action, Action handler) => _handlers[action] = handler;

    public bool IsAvailable(OrbitAction action) => _handlers.ContainsKey(action);

    public void Perform(OrbitAction action)
    {
        if (!_handlers.TryGetValue(action, out var handler))
        {
            Log.Info($"Action {action.Id()} is not available yet");
            Unavailable?.Invoke(action);
            return;
        }
        Log.Info($"Action {action.Id()}");
        handler();
    }
}
