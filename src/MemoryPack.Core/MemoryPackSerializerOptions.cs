namespace MemoryPack
{
    public record MemoryPackSerializerOptions
    {
        // Default is Utf8
        public static readonly MemoryPackSerializerOptions Default = new MemoryPackSerializerOptions { StringEncoding = StringEncoding.Utf8, MaxDepth = 512 };

        public static readonly MemoryPackSerializerOptions Utf8 = Default with { StringEncoding = StringEncoding.Utf8 };
        public static readonly MemoryPackSerializerOptions Utf16 = Default with { StringEncoding = StringEncoding.Utf16 };

        public StringEncoding StringEncoding { get; init; }
        public IServiceProvider? ServiceProvider { get; init; }

        // Defer threshold for CircularReference types. Past this depth, the writer queues bodies
        // instead of recursing inline, then drains the queue at the top level.
        public int MaxDepth { get; init; }
    }

    public enum StringEncoding : byte
    {
        Utf16,
        Utf8,
    }
}

#if !NET5_0_OR_GREATER

namespace System.Runtime.CompilerServices
{
    internal sealed class IsExternalInit
    {
    }
}

#endif
