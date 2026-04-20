using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace HandsUp.HandsUpCode.Multiplayer.Messages;

public struct RaiseHandApprovalResponseMessage : INetMessage
{
    public string RequestId;
    public bool Approved;

    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(RequestId);
        writer.WriteBool(Approved);
    }

    public void Deserialize(PacketReader reader)
    {
        RequestId = reader.ReadString();
        Approved = reader.ReadBool();
    }
}
