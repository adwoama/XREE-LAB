// Stub to prevent Meta Voice SDK WebSocket compile errors
// This file shadows the broken NativeWebSocketWrapper.cs in the package cache
// Place in Assets/Scripts/VoiceSDKStub/ with higher compile priority

#if false // Never compile; this just prevents errors
namespace Meta.Voice.Net.WebSockets
{
    public class NativeWebSocketWrapper { }
}
#endif
