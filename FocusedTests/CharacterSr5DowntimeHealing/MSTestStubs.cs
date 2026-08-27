namespace Microsoft.VisualStudio.TestTools.UnitTesting;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class TestClassAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestMethodAttribute : Attribute;

internal static class Assert
{
    public static void IsTrue(bool condition, string? message = null)
    {
        if (!condition) throw new InvalidOperationException(message ?? "Expected true.");
    }

    public static void IsFalse(bool condition, string? message = null)
    {
        if (condition) throw new InvalidOperationException(message ?? "Expected false.");
    }

    public static void AreEqual<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                message ?? $"Expected <{expected}> but observed <{actual}>.");
    }

    public static void AreNotEqual<T>(T notExpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            throw new InvalidOperationException(
                message ?? $"Did not expect <{actual}>.");
    }
}
