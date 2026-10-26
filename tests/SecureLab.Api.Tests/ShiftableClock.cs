namespace SecureLab.Api.Tests;

// Годинник для тестів строку сеансу: стартує з поточного часу, а тест пересуває його вперед.
// Так тест перевіряє 20 хвилин без жодного очікування.
internal sealed class ShiftableClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
