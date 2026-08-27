using System;

namespace DingoNetworkVoice
{
    public enum VoiceSessionState
    {
        Inactive,
        Listening,
        Capturing,
        Disposed
    }

    public enum VoiceErrorCode
    {
        None,
        InvalidState,
        InvalidPeer,
        InvalidFrame,
        CaptureFailure,
        TransportFailure,
        DecodeFailure,
        PlaybackFailure
    }

    public readonly struct VoicePeerId : IEquatable<VoicePeerId>
    {
        private readonly string _value;

        public string Value => _value ?? string.Empty;
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        public VoicePeerId(string value)
        {
            _value = value?.Trim() ?? string.Empty;
        }

        public static bool TryParse(string value, out VoicePeerId peerId)
        {
            peerId = new VoicePeerId(value);
            return peerId.IsValid;
        }

        public bool Equals(VoicePeerId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is VoicePeerId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(VoicePeerId left, VoicePeerId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VoicePeerId left, VoicePeerId right)
        {
            return !left.Equals(right);
        }
    }

    public class VoiceError
    {
        public readonly VoiceErrorCode Code;
        public readonly string Message;

        public VoiceError(VoiceErrorCode code, string message)
        {
            Code = code;
            Message = message ?? string.Empty;
        }

        public override string ToString()
        {
            return $"{Code}: {Message}";
        }
    }

    public class VoiceCommandResult
    {
        public readonly bool Accepted;
        public readonly VoiceError Error;

        private VoiceCommandResult(bool accepted, VoiceError error)
        {
            Accepted = accepted;
            Error = error;
        }

        public static VoiceCommandResult Success()
        {
            return new VoiceCommandResult(true, null);
        }

        public static VoiceCommandResult Rejected(
            VoiceErrorCode code,
            string message)
        {
            return new VoiceCommandResult(
                false,
                new VoiceError(code, message));
        }

        public static VoiceCommandResult Rejected(VoiceError error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            return new VoiceCommandResult(false, error);
        }
    }

    public class VoiceEncodedFrame
    {
        private readonly byte[] _payload;

        public ReadOnlyMemory<byte> Payload => _payload;

        public VoiceEncodedFrame(byte[] payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.Length == 0)
            {
                throw new ArgumentException(
                    "Encoded voice frame cannot be empty.",
                    nameof(payload));
            }

            _payload = new byte[payload.Length];
            Buffer.BlockCopy(payload, 0, _payload, 0, payload.Length);
        }
    }

    public class VoicePacket
    {
        public readonly uint Sequence;
        public readonly VoiceEncodedFrame Frame;

        public VoicePacket(uint sequence, VoiceEncodedFrame frame)
        {
            if (sequence == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequence),
                    "Voice packet sequence must be greater than zero.");
            }

            Sequence = sequence;
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }
    }

    public class VoiceIncomingPacket
    {
        public readonly VoicePeerId SenderId;
        public readonly VoicePacket Packet;

        public VoiceIncomingPacket(VoicePeerId senderId, VoicePacket packet)
        {
            if (!senderId.IsValid)
            {
                throw new ArgumentException(
                    "Voice packet sender must be valid.",
                    nameof(senderId));
            }

            SenderId = senderId;
            Packet = packet ?? throw new ArgumentNullException(nameof(packet));
        }
    }

    public class VoicePcmFrame
    {
        private readonly short[] _samples;

        public readonly int SampleRate;
        public readonly int Channels;

        public ReadOnlyMemory<short> Samples => _samples;

        public VoicePcmFrame(
            short[] samples,
            int sampleRate,
            int channels)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (samples.Length == 0)
            {
                throw new ArgumentException(
                    "PCM frame cannot be empty.",
                    nameof(samples));
            }

            if (sampleRate < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            if (channels < 1 || channels > 8)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(channels),
                    "PCM channel count must be between 1 and 8.");
            }

            _samples = new short[samples.Length];
            Buffer.BlockCopy(
                samples,
                0,
                _samples,
                0,
                samples.Length * sizeof(short));
            SampleRate = sampleRate;
            Channels = channels;
        }
    }

    public class VoicePeerState
    {
        public readonly VoicePeerId PeerId;
        public readonly bool IsMuted;
        public readonly int ReceivedFrames;
        public readonly int DroppedFrames;

        public VoicePeerState(
            VoicePeerId peerId,
            bool isMuted,
            int receivedFrames,
            int droppedFrames)
        {
            PeerId = peerId;
            IsMuted = isMuted;
            ReceivedFrames = receivedFrames;
            DroppedFrames = droppedFrames;
        }
    }
}
