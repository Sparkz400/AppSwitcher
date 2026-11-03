namespace AppSwitcher.Services;

// Fixed stripes preserve same-key exclusion without retaining every historical
// app/profile identity. Collisions only serialize independent operations.
internal sealed class BoundedOperationGates
{
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    internal SemaphoreSlim For(string key) => _gates[(uint)StringComparer.Ordinal.GetHashCode(key) % (uint)_gates.Length];
}
