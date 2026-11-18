namespace Multiplayer.Common.Networking.Packet;

public static class ClientDiagnosticsLimits
{
    /// <summary>Kept below the 64 KiB single-packet limit; larger journals are sent over several batches.</summary>
    public const int MaxPayload = 48 * 1024;

    public const int MaxBatchEntries = 8;

    public const int MaxMessageLength = 1024;
    public const int MaxStackLength = 2048;
}

/// <summary>
/// A batch of errors/warnings captured by a client, as UTF-8 JSON. The server only validates and relays it.
/// </summary>
[PacketDefinition(Packets.Client_Diagnostics)]
public record struct ClientDiagnosticsPacket(long sequence, byte[] payload) : IPacket
{
    public long sequence = sequence;
    public byte[] payload = payload;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref sequence);
        buf.BindBytes(ref payload, maxLength: ClientDiagnosticsLimits.MaxPayload);
    }
}

/// <summary>
/// The server relays a client's batch to the host. The host writes it to disk; nobody else parses it.
/// </summary>
[PacketDefinition(Packets.Server_Diagnostics)]
public record struct ServerDiagnosticsPacket(int playerId, long sequence, byte[] payload) : IPacket
{
    public int playerId = playerId;
    public long sequence = sequence;
    public byte[] payload = payload;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref playerId);
        buf.Bind(ref sequence);
        buf.BindBytes(ref payload, maxLength: ClientDiagnosticsLimits.MaxPayload);
    }
}
