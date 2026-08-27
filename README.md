# DingoNetworkVoice

Provider-neutral voice lifecycle and routing core for Unity projects. The
runtime assembly has no UnityEngine, Steamworks, networking-provider, or product
dependency.

## Runtime ownership

Create one `VoiceSession` for one local voice context. The session owns and
disposes its `IVoiceCapture`, `IVoiceDecoder`, and `IVoicePlayback` providers.
It borrows `IVoiceTransport`, because the transport commonly adapts a shared
application networking client whose lifecycle is owned by the composition root.

The owner calls:

1. `Activate()` after networking/lobby voice can receive packets.
2. `StartCapture()` and `StopCapture()` according to push-to-talk or product
   policy.
3. `Tick()` once per application frame on the same thread as all providers.
4. `RemovePeer()` when a remote participant leaves the active roster.
5. `Deactivate()` when leaving the voice context and `Dispose()` at shutdown.

## Real-time policy

`VoiceSessionConfiguration` bounds capture work, incoming work, encoded frame
size, and queued incoming packets. When the incoming queue is full, the oldest
packet is discarded to preserve latency. Duplicate and out-of-order sequences
are discarded per peer. Muted peers advance their sequence state but skip
decode/playback, so unmuting cannot replay stale audio.

The core does not choose a codec, sample rate, microphone API, transport flags,
jitter strategy, or Unity playback mechanism. A concrete realization supplies
those providers and maps its stable network identity to `VoicePeerId`.
