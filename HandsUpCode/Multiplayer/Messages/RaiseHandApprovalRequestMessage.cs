using HandsUp.HandsUpCode.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace HandsUp.HandsUpCode.Multiplayer.Messages;

public struct RaiseHandApprovalRequestMessage : INetMessage
{
    public string RequestId;
    public ulong InitiatorNetId;
    public RaiseHandActionKind ActionKind;
    public string InitiatorLabel;

    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(RequestId);
        writer.WriteULong(InitiatorNetId, 64);
        writer.WriteEnum(ActionKind);
        writer.WriteString(InitiatorLabel);
    }

    public void Deserialize(PacketReader reader)
    {
        RequestId = reader.ReadString();
        InitiatorNetId = reader.ReadULong(64);
        ActionKind = reader.ReadEnum<RaiseHandActionKind>();
        InitiatorLabel = reader.ReadString();
    }
}
