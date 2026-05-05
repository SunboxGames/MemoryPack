using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

namespace MemoryPack;

public static class MemoryPackWriterOptionalStatePool
{
    static readonly ConcurrentQueue<MemoryPackWriterOptionalState> queue = new ConcurrentQueue<MemoryPackWriterOptionalState>();

    public static MemoryPackWriterOptionalState Rent(MemoryPackSerializerOptions? options)
    {
        if (!queue.TryDequeue(out var state))
        {
            state = new MemoryPackWriterOptionalState();
        }

        state.Init(options);
        return state;
    }

    internal static void Return(MemoryPackWriterOptionalState state)
    {
        state.Reset();
        queue.Enqueue(state);
    }
}

// Type-erased dispatcher emitted as a singleton per CircularReference type by the source generator.
// Lets the writer drain loop invoke a type-specific SerializeBody without knowing the concrete T at the call site.
public abstract class MemoryPackDeferredBodyWriter
{
#if NET7_0_OR_GREATER
    public abstract void WriteBody<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, object value, uint id)
        where TBufferWriter : IBufferWriter<byte>;
#else
    public abstract void WriteBody<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, object value, uint id)
        where TBufferWriter : class, IBufferWriter<byte>;
#endif
}

public struct DeferredEntry
{
    public uint Id;
    public object Value;
    public MemoryPackDeferredBodyWriter Dispatcher;
}

public sealed class MemoryPackWriterOptionalState : IDisposable
{
    internal static readonly MemoryPackWriterOptionalState NullState = new MemoryPackWriterOptionalState(true);

    uint nextId;
    readonly Dictionary<object, uint> objectToRef;
    readonly Queue<DeferredEntry> deferQueue;

    public MemoryPackSerializerOptions Options { get; private set; }

    // Shared recursion depth — held on the heap-allocated state rather than the ref-struct writer
    // so that temp sub-writers (used by VersionTolerant/CircularReference offset computation)
    // share the same depth count as their parent. Otherwise tempWriter.depth would reset to 0 on
    // construction and the defer threshold would never trip inside a temp-buffered body write.
    public int Depth;

    // True once any CircularReference object has been assigned an id during this serialize call.
    // Used by the top-level drain loop to decide whether to emit deferred-block stream + sentinel.
    // Stays false for serializations that don't involve any CircularReference type, so wire format
    // for plain Object types is byte-identical to pre-defer behavior.
    public bool HasReferences => nextId > 0;

    internal MemoryPackWriterOptionalState()
    {
        objectToRef = new Dictionary<object, uint>(ReferenceEqualityComparer.Instance);
        deferQueue = new Queue<DeferredEntry>();
        Options = null!;
        nextId = 0;
    }

    MemoryPackWriterOptionalState(bool _)
    {
        objectToRef = null!;
        deferQueue = null!;
        Options = MemoryPackSerializerOptions.Default;
        nextId = 0;
    }

    internal void Init(MemoryPackSerializerOptions? options)
    {
        Options = options ?? MemoryPackSerializerOptions.Default;
    }

    public void Reset()
    {
        objectToRef.Clear();
        deferQueue.Clear();
        Options = null!;
        nextId = 0;
        Depth = 0;
    }

    public (bool existsReference, uint id) GetOrAddReference(object value)
    {
#if NET7_0_OR_GREATER
        ref var id = ref CollectionsMarshal.GetValueRefOrAddDefault(objectToRef, value, out var exists);
        if (exists)
        {
            return (true, id);
        }
        else
        {
            id = nextId++;
            return (false, id);
        }
#else
        if (objectToRef.TryGetValue(value, out var id))
        {
            return (true, id);
        }
        else
        {
            id = nextId++;
            objectToRef.Add(value, id);
            return (false, id);
        }
#endif
    }

    public void EnqueueDeferred(uint id, object value, MemoryPackDeferredBodyWriter dispatcher)
    {
        deferQueue.Enqueue(new DeferredEntry { Id = id, Value = value, Dispatcher = dispatcher });
    }

    public bool TryDequeueDeferred(out DeferredEntry entry)
    {
        return deferQueue.TryDequeue(out entry);
    }

    void IDisposable.Dispose()
    {
        MemoryPackWriterOptionalStatePool.Return(this);
    }

    // ReferenceEqualityComparer is exsits in .NET 6 but NetStandard 2.1 does not.
    sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        ReferenceEqualityComparer() { }

        public static ReferenceEqualityComparer Instance { get; } = new ReferenceEqualityComparer();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
