using System;

namespace DingoNetworkVoice
{
    public interface IVoiceCapture : IDisposable
    {
        VoiceCommandResult StartCapture();
        VoiceCommandResult StopCapture();

        bool TryCapture(
            out VoiceEncodedFrame frame,
            out VoiceError error);
    }

    public interface IVoiceTransport
    {
        event Action<VoiceIncomingPacket> PacketReceived;

        VoiceCommandResult Broadcast(VoicePacket packet);
    }

    public interface IVoiceDecoder : IDisposable
    {
        bool TryDecode(
            VoiceEncodedFrame encodedFrame,
            out VoicePcmFrame pcmFrame,
            out VoiceError error);
    }

    public interface IVoicePlayback : IDisposable
    {
        VoiceCommandResult Submit(
            VoicePeerId peerId,
            VoicePcmFrame pcmFrame);

        void RemovePeer(VoicePeerId peerId);
        void Clear();
        void Tick();
    }
}
