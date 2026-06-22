using HandsUp.HandsUpCode.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace HandsUp.HandsUpCode.Multiplayer.Messages;

public struct RaiseHandExecuteMessage : INetMessage
{
    public RaiseHandActionKind ActionKind;
    public string? RunJson;
    public string? RoomJson;
    public string? SourceRoomType;
    public string? CombatStateJson;
    public string? RestoreHint;

    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteEnum(ActionKind);
        writer.WriteString(RunJson ?? string.Empty);
        writer.WriteString(RoomJson ?? string.Empty);
        writer.WriteString(SourceRoomType ?? string.Empty);
        writer.WriteString(CombatStateJson ?? string.Empty);
        writer.WriteString(RestoreHint ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        ActionKind = reader.ReadEnum<RaiseHandActionKind>();
        RunJson = reader.ReadString();
        RoomJson = reader.ReadString();
        SourceRoomType = reader.ReadString();
        CombatStateJson = reader.ReadString();
        RestoreHint = reader.ReadString();
    }
}
