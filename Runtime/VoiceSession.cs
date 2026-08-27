using System;
using System.Collections.Generic;

namespace DingoNetworkVoice
{
    public class VoiceSession : IDisposable
    {
        private readonly IVoiceCapture _capture;
        private readonly IVoiceTransport _transport;
        private readonly IVoiceDecoder _decoder;
        private readonly IVoicePlayback _playback;
        private readonly VoiceSessionConfiguration _configuration;
        private readonly Queue<VoiceIncomingPacket> _incomingPackets = new();
        private readonly HashSet<VoicePeerId> _knownPeers = new();
        private readonly HashSet<VoicePeerId> _mutedPeers = new();
        private readonly Dictionary<VoicePeerId, uint> _lastSequences = new();
        private readonly Dictionary<VoicePeerId, int> _receivedFrames = new();
        private readonly Dictionary<VoicePeerId, int> _droppedFrames = new();

        private uint _nextOutgoingSequence = 1;

        public VoiceSessionState State { get; private set; }
        public VoiceError LastError { get; private set; }
        public int QueuedIncomingFrames => _incomingPackets.Count;
        public int DroppedIncomingFrames { get; private set; }

        public event Action<VoiceSessionState> StateChanged;
        public event Action<VoicePeerState> PeerStateChanged;
        public event Action<VoicePeerId> PeerRemoved;
        public event Action<VoiceError> ErrorRaised;

        public VoiceSession(
            IVoiceCapture capture,
            IVoiceTransport transport,
            IVoiceDecoder decoder,
            IVoicePlayback playback,
            VoiceSessionConfiguration configuration = null)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _transport = transport
                         ?? throw new ArgumentNullException(nameof(transport));
            _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
            _playback = playback
                        ?? throw new ArgumentNullException(nameof(playback));
            _configuration = configuration ?? new VoiceSessionConfiguration();
            State = VoiceSessionState.Inactive;
        }

        public VoiceCommandResult Activate()
        {
            if (State == VoiceSessionState.Disposed)
            {
                return Reject(
                    VoiceErrorCode.InvalidState,
                    "Disposed voice session cannot be activated.");
            }

            if (State != VoiceSessionState.Inactive)
            {
                return Reject(
                    VoiceErrorCode.InvalidState,
                    "Voice session is already active.");
            }

            ClearError();
            _transport.PacketReceived += OnPacketReceived;
            SetState(VoiceSessionState.Listening);
            return VoiceCommandResult.Success();
        }

        public VoiceCommandResult Deactivate()
        {
            if (State == VoiceSessionState.Disposed)
            {
                return Reject(
                    VoiceErrorCode.InvalidState,
                    "Disposed voice session cannot be deactivated.");
            }

            if (State == VoiceSessionState.Inactive)
            {
                return Reject(
                    VoiceErrorCode.InvalidState,
                    "Voice session is not active.");
            }

            VoiceError stopError = null;
            if (State == VoiceSessionState.Capturing)
            {
                var stopResult = _capture.StopCapture();
                if (!stopResult.Accepted)
                {
                    stopError = stopResult.Error;
                }
            }

            _transport.PacketReceived -= OnPacketReceived;
            ResetRuntimeState();
            SetState(VoiceSessionState.Inactive);

            if (stopError != null)
            {
                RaiseError(stopError);
                return VoiceCommandResult.Rejected(stopError);
            }

            return VoiceCommandResult.Success();
        }

        public VoiceCommandResult StartCapture()
        {
            if (State != VoiceSessionState.Listening)
            {
                return Reject(
                    VoiceErrorCode.InvalidState,
                    "Voice capture can start only while the session is listening.");
            }

            var result = _capture.StartCapture();
            if (!result.Accepted)
            {
                RaiseError(result.Error);
                return result;
            }

            SetState(VoiceSessionState.Capturing);
            return VoiceCommandResult.Success();
        }

        public VoiceCommandResult StopCapture()
        {
            if (State != VoiceSessionState.Capturing)
            {
                return Reject(
                    VoiceErrorCode.InvalidState,
                    "Voice capture is not active.");
            }

            var result = _capture.StopCapture();
            if (!result.Accepted)
            {
                RaiseError(result.Error);
                return result;
            }

            SetState(VoiceSessionState.Listening);
            return VoiceCommandResult.Success();
        }

        public void Tick()
        {
            if (State == VoiceSessionState.Inactive
                || State == VoiceSessionState.Disposed)
            {
                return;
            }

            if (State == VoiceSessionState.Capturing)
            {
                TickCapture();
            }

            TickIncoming();
            _playback.Tick();
        }

        public VoiceCommandResult SetPeerMuted(
            VoicePeerId peerId,
            bool isMuted)
        {
            if (State == VoiceSessionState.Inactive
                || State == VoiceSessionState.Disposed)
            {
                return Reject(
                    VoiceErrorCode.InvalidState,
                    "Peer mute state requires an active voice session.");
            }

            if (!peerId.IsValid)
            {
                return Reject(
                    VoiceErrorCode.InvalidPeer,
                    "Voice peer ID is invalid.");
            }

            _knownPeers.Add(peerId);
            if (isMuted)
            {
                _mutedPeers.Add(peerId);
                _playback.RemovePeer(peerId);
            }
            else
            {
                _mutedPeers.Remove(peerId);
            }

            PublishPeerState(peerId);
            return VoiceCommandResult.Success();
        }

        public bool RemovePeer(VoicePeerId peerId)
        {
            if (!peerId.IsValid || !_knownPeers.Remove(peerId))
            {
                return false;
            }

            _mutedPeers.Remove(peerId);
            _lastSequences.Remove(peerId);
            _receivedFrames.Remove(peerId);
            _droppedFrames.Remove(peerId);
            _playback.RemovePeer(peerId);
            PeerRemoved?.Invoke(peerId);
            return true;
        }

        public bool TryGetPeerState(
            VoicePeerId peerId,
            out VoicePeerState peerState)
        {
            if (!_knownPeers.Contains(peerId))
            {
                peerState = null;
                return false;
            }

            peerState = CreatePeerState(peerId);
            return true;
        }

        public IReadOnlyList<VoicePeerState> GetPeerStates()
        {
            var result = new VoicePeerState[_knownPeers.Count];
            var index = 0;
            foreach (var peerId in _knownPeers)
            {
                result[index++] = CreatePeerState(peerId);
            }

            return result;
        }

        public void ClearError()
        {
            LastError = null;
        }

        public void Dispose()
        {
            if (State == VoiceSessionState.Disposed)
            {
                return;
            }

            if (State != VoiceSessionState.Inactive)
            {
                _transport.PacketReceived -= OnPacketReceived;
                if (State == VoiceSessionState.Capturing)
                {
                    _capture.StopCapture();
                }

                ResetRuntimeState();
            }

            _capture.Dispose();
            _decoder.Dispose();
            _playback.Dispose();
            SetState(VoiceSessionState.Disposed);
            StateChanged = null;
            PeerStateChanged = null;
            PeerRemoved = null;
            ErrorRaised = null;
        }

        private void TickCapture()
        {
            for (var i = 0;
                 i < _configuration.MaxCaptureFramesPerTick;
                 i++)
            {
                if (!_capture.TryCapture(out var frame, out var error))
                {
                    if (error != null)
                    {
                        _capture.StopCapture();
                        SetState(VoiceSessionState.Listening);
                        RaiseError(error);
                    }

                    return;
                }

                if (frame == null
                    || frame.Payload.Length
                    > _configuration.MaxEncodedFrameBytes)
                {
                    RaiseError(new VoiceError(
                        VoiceErrorCode.InvalidFrame,
                        "Captured voice frame is missing or exceeds the configured limit."));
                    continue;
                }

                var packet = new VoicePacket(
                    TakeNextOutgoingSequence(),
                    frame);
                var result = _transport.Broadcast(packet);
                if (!result.Accepted)
                {
                    RaiseError(result.Error);
                }
            }
        }

        private void TickIncoming()
        {
            var processed = 0;
            while (processed < _configuration.MaxIncomingFramesPerTick
                   && _incomingPackets.Count > 0)
            {
                ProcessIncoming(_incomingPackets.Dequeue());
                processed++;
            }
        }

        private void ProcessIncoming(VoiceIncomingPacket incoming)
        {
            var peerId = incoming.SenderId;
            _knownPeers.Add(peerId);

            if (incoming.Packet.Frame.Payload.Length
                > _configuration.MaxEncodedFrameBytes)
            {
                RecordDropped(peerId);
                RaiseError(new VoiceError(
                    VoiceErrorCode.InvalidFrame,
                    $"Voice frame from '{peerId}' exceeds the configured limit."));
                return;
            }

            if (_lastSequences.TryGetValue(peerId, out var lastSequence)
                && !IsNewerSequence(
                    incoming.Packet.Sequence,
                    lastSequence))
            {
                RecordDropped(peerId);
                return;
            }

            _lastSequences[peerId] = incoming.Packet.Sequence;
            RecordReceived(peerId);
            if (_mutedPeers.Contains(peerId))
            {
                return;
            }

            if (!_decoder.TryDecode(
                    incoming.Packet.Frame,
                    out var pcmFrame,
                    out var decodeError))
            {
                RecordDropped(peerId);
                RaiseError(decodeError ?? new VoiceError(
                    VoiceErrorCode.DecodeFailure,
                    $"Voice frame from '{peerId}' could not be decoded."));
                return;
            }

            var playbackResult = _playback.Submit(peerId, pcmFrame);
            if (!playbackResult.Accepted)
            {
                RecordDropped(peerId);
                RaiseError(playbackResult.Error);
            }
        }

        private void OnPacketReceived(VoiceIncomingPacket incoming)
        {
            if (State == VoiceSessionState.Inactive
                || State == VoiceSessionState.Disposed
                || incoming == null)
            {
                return;
            }

            if (_incomingPackets.Count
                == _configuration.IncomingQueueCapacity)
            {
                var dropped = _incomingPackets.Dequeue();
                DroppedIncomingFrames++;
                RecordDropped(dropped.SenderId);
            }

            _incomingPackets.Enqueue(incoming);
        }

        private void ResetRuntimeState()
        {
            _incomingPackets.Clear();
            _knownPeers.Clear();
            _mutedPeers.Clear();
            _lastSequences.Clear();
            _receivedFrames.Clear();
            _droppedFrames.Clear();
            DroppedIncomingFrames = 0;
            _nextOutgoingSequence = 1;
            _playback.Clear();
        }

        private uint TakeNextOutgoingSequence()
        {
            var result = _nextOutgoingSequence;
            _nextOutgoingSequence = result == uint.MaxValue
                ? 1
                : result + 1;
            return result;
        }

        private void RecordReceived(VoicePeerId peerId)
        {
            _receivedFrames.TryGetValue(peerId, out var count);
            _receivedFrames[peerId] = count + 1;
            PublishPeerState(peerId);
        }

        private void RecordDropped(VoicePeerId peerId)
        {
            _knownPeers.Add(peerId);
            _droppedFrames.TryGetValue(peerId, out var count);
            _droppedFrames[peerId] = count + 1;
            PublishPeerState(peerId);
        }

        private VoicePeerState CreatePeerState(VoicePeerId peerId)
        {
            _receivedFrames.TryGetValue(peerId, out var received);
            _droppedFrames.TryGetValue(peerId, out var dropped);
            return new VoicePeerState(
                peerId,
                _mutedPeers.Contains(peerId),
                received,
                dropped);
        }

        private void PublishPeerState(VoicePeerId peerId)
        {
            PeerStateChanged?.Invoke(CreatePeerState(peerId));
        }

        private VoiceCommandResult Reject(
            VoiceErrorCode code,
            string message)
        {
            var result = VoiceCommandResult.Rejected(code, message);
            RaiseError(result.Error);
            return result;
        }

        private void RaiseError(VoiceError error)
        {
            if (error == null)
            {
                return;
            }

            LastError = error;
            ErrorRaised?.Invoke(error);
        }

        private void SetState(VoiceSessionState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            StateChanged?.Invoke(state);
        }

        private static bool IsNewerSequence(uint sequence, uint previous)
        {
            return unchecked((int)(sequence - previous)) > 0;
        }
    }
}
