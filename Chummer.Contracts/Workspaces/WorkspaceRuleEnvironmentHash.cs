using System.Buffers.Binary;
using System.Numerics;

namespace Chummer.Contracts.Workspaces;

/// <summary>Serializable SHA-256 state for private, server-owned bounded archive verification.
/// Never accept this state from a client. The enclosing durable CAS binds it to immutable chunks.</summary>
public sealed class WorkspaceRuleEnvironmentHash
{
    public uint[] Words { get; set; } = [0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19];
    public byte[] Tail { get; set; } = new byte[64];
    public long Length { get; set; }
    private static readonly uint[] K = [
        0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2];
    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (Words.Length != 8 || Tail.Length != 64 || Length < 0 || Length > int.MaxValue - bytes.Length)
            throw new InvalidDataException("Invalid private hash checkpoint.");
        int offset = (int)(Length % 64);
        Length += bytes.Length;
        if (offset != 0)
        {
            int take = Math.Min(64 - offset, bytes.Length);
            bytes[..take].CopyTo(Tail.AsSpan(offset)); bytes = bytes[take..];
            if (offset + take < 64) return;
            Block(Tail);
        }
        while (bytes.Length >= 64) { Block(bytes[..64]); bytes = bytes[64..]; }
        Array.Clear(Tail);
        bytes.CopyTo(Tail);
    }
    public string Digest()
    {
        var copy = new WorkspaceRuleEnvironmentHash { Words = (uint[])Words.Clone(), Tail = (byte[])Tail.Clone(), Length = Length };
        Span<byte> padding = stackalloc byte[128]; padding.Clear(); padding[0] = 0x80;
        int count = (Length % 64 < 56 ? 56 : 120) - (int)(Length % 64);
        BinaryPrimitives.WriteUInt64BigEndian(padding.Slice(count, 8), checked((ulong)Length * 8));
        copy.Append(padding[..(count + 8)]);
        Span<byte> result = stackalloc byte[32];
        for (int i = 0; i < 8; i++) BinaryPrimitives.WriteUInt32BigEndian(result.Slice(i * 4, 4), copy.Words[i]);
        return Convert.ToHexStringLower(result);
    }
    private void Block(ReadOnlySpan<byte> bytes)
    {
        Span<uint> w = stackalloc uint[64];
        for (int i = 0; i < 16; i++) w[i] = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(i * 4, 4));
        unchecked
        {
            for (int i = 16; i < 64; i++)
            {
                uint x = w[i-15], y = w[i-2];
                w[i] = w[i-16] + (BitOperations.RotateRight(x,7) ^ BitOperations.RotateRight(x,18) ^ (x >> 3))
                    + w[i-7] + (BitOperations.RotateRight(y,17) ^ BitOperations.RotateRight(y,19) ^ (y >> 10));
            }
            uint a=Words[0],b=Words[1],c=Words[2],d=Words[3],e=Words[4],f=Words[5],g=Words[6],h=Words[7];
            for (int i = 0; i < 64; i++)
            {
                uint t1=h+(BitOperations.RotateRight(e,6)^BitOperations.RotateRight(e,11)^BitOperations.RotateRight(e,25))+((e&f)^(~e&g))+K[i]+w[i];
                uint t2=(BitOperations.RotateRight(a,2)^BitOperations.RotateRight(a,13)^BitOperations.RotateRight(a,22))+((a&b)^(a&c)^(b&c));
                h=g;g=f;f=e;e=d+t1;d=c;c=b;b=a;a=t1+t2;
            }
            Words[0]+=a;Words[1]+=b;Words[2]+=c;Words[3]+=d;Words[4]+=e;Words[5]+=f;Words[6]+=g;Words[7]+=h;
        }
    }
}
