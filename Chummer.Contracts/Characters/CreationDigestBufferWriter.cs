using System.Buffers;
using System.Security.Cryptography;

namespace Chummer.Contracts.Characters;

// Hash exactly the bytes committed by Utf8JsonWriter without retaining another
// full copy of a Creation catalog. Each invocation owns its hash and rental;
// neither domain values nor digests are cached between calls.
internal sealed class CreationDigestBufferWriter : IBufferWriter<byte>, IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(4096);

    public void Advance(int count)
    {
        if ((uint)count > (uint)_buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        _hash.AppendData(_buffer.AsSpan(0, count));
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer;
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer;
    }

    private void EnsureCapacity(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        if (sizeHint <= _buffer.Length) return;
        byte[] replacement = ArrayPool<byte>.Shared.Rent(sizeHint);
        ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
        _buffer = replacement;
    }

    public string GetDigest() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public void Dispose()
    {
        _hash.Dispose();
        ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
    }
}
