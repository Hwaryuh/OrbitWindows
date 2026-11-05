using Orbit.Core.Input;

namespace Orbit.Tests;

/// <summary>A pretend key state for the matcher: these keys are down.</summary>
internal sealed class Keys(params uint[] down) : IKeyState
{
    public bool IsDown(uint vk) => down.Contains(vk);
}
