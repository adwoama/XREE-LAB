// Minimal stub to satisfy Meta Voice SDK's WebSocket dependency
// This prevents compile errors in NativeWebSocketWrapper.cs

#pragma warning disable CS0067 // Event never used

namespace Meta.Net.NativeWebSocket
{
    public enum WebSocketState { Connecting, Open, Closing, Closed }
    
    public class WebSocket
    {
        public WebSocketState State { get; set; }
        public event System.Action OnOpen;
        public event System.Action<byte[]> OnMessage;
        public event System.Action<string> OnError;
        public event System.Action<WebSocketCloseCode> OnClose;
        
        public WebSocket(string url) { }
        public System.Threading.Tasks.Task Connect() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task Close() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task Send(byte[] data) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task SendText(string text) => System.Threading.Tasks.Task.CompletedTask;
        public void DispatchMessageQueue() { }
    }
    
    public enum WebSocketCloseCode { Normal = 1000 }
}

namespace Meta.Voice.Net.WebSockets
{
    // Stub for WitWebSocketClient referenced by WitConfiguration
    public class WitWebSocketClient
    {
        public WitWebSocketClient() { }
    }
}
