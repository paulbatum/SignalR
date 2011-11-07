using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading.Tasks;

namespace SignalR.Client.Transports
{
    public class WcfWebSocketClient : IDisposable
    {
        DuplexChannelFactory<IWebSocket> duplexChannelFactory;
        IWebSocket client;
        ClientCallback callback;

        public Action<string> OnMessage { get; set; }
        public Action OnClose { get; set; }

        public WcfWebSocketClient(Uri uri, string subprotocol = null)
        {
            bool https = uri.Scheme == "wss";
            duplexChannelFactory = new DuplexChannelFactory<IWebSocket>(typeof(ClientCallback), CreateWebSocketBinding(https: https, subprotocol: subprotocol), new EndpointAddress(RewriteToHttp(uri)));            
        }

        public Task Open()
        {
            callback = new ClientCallback(this);
            client = duplexChannelFactory.CreateChannel(new InstanceContext(callback));

            var channel = ((IChannel)client);
            channel.Closed += channel_Closed;
            return Task.Factory.FromAsync(channel.BeginOpen, channel.EndOpen, null);
            
        }

        public async Task Close()
        {
            var channel = ((IChannel)client);
            await Task.Factory.FromAsync(channel.BeginClose, channel.EndClose, null);
            //try
            //{                
                
            //}
            //catch
            //{
            //    // I have to do this because IIS seems to be terminating the connection prematurely.
            //    channel.Abort();
            //}
        }

        public void Dispose()
        {
            if (duplexChannelFactory != null)
                duplexChannelFactory.Abort();
        }

        public Task Send(string message)
        {
            var m = ByteStreamMessage.CreateMessage(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)));
            m.Properties[WebSocketMessageProperty.Name] = new WebSocketMessageProperty() { OutgoingMessageType = WebSocketMessageType.Text };
            return client.OnMessage(m);
        }

        public WebSocketState State
        {
            get
            {
                if (client == null)
                    return WebSocketState.None;

                var channel = ((IChannel)client);

                switch (channel.State)
                {
                    case CommunicationState.Closed:
                        return WebSocketState.Closed;
                    case CommunicationState.Closing:
                        // TODO: Implement this properly
                        return WebSocketState.CloseReceived;
                    case CommunicationState.Opening:
                    case CommunicationState.Created:
                        return WebSocketState.None;
                    case CommunicationState.Faulted:
                        return WebSocketState.Aborted;
                    case CommunicationState.Opened:
                        return WebSocketState.Open;
                    default:
                        throw new InvalidOperationException("Unknown CommunicationState.");
                }
            }
        }

        void channel_Closed(object sender, EventArgs e)
        {
            if (OnClose != null)
                OnClose();
        }

        public Binding CreateWebSocketBinding(bool https, int sendBufferSize = 0, int receiveBufferSize = 0, string subprotocol = null)
        {
            var encoder = new ByteStreamMessageEncodingBindingElement();
            encoder.MessageVersion = MessageVersion.None;

            HttpTransportBindingElement transport = https ? new HttpsTransportBindingElement() : new HttpTransportBindingElement();
            transport.WebSocketSettings.ConnectionMode = WebSocketConnectionMode.Required;

            if (subprotocol != null)
                transport.WebSocketSettings.SubProtocol = subprotocol;

            if (sendBufferSize != 0)
                transport.WebSocketSettings.SendBufferSize = sendBufferSize;

            if (receiveBufferSize != 0)
                transport.WebSocketSettings.ReceiveBufferSize = receiveBufferSize;

            Binding binding = new CustomBinding(encoder, transport);
            binding.ReceiveTimeout = TimeSpan.FromHours(24);

            return binding;
        }

        static Uri RewriteToHttp(Uri uri)
        {
            // No changes to relative uri's
            if (uri.IsAbsoluteUri == false)
                return uri;

            switch (uri.Scheme)
            {
                case "ws":
                    return new Uri("http" + uri.AbsoluteUri.Substring(2));
                case "wss":
                    return new Uri("https" + uri.AbsoluteUri.Substring(3));
                case "http":
                case "https":
                    return uri;
                default:
                    throw new ArgumentException("Must supply a websocket address (ws:// or wss://) or a http address (http:// or https://).");
            }
        }


        class ClientCallback : IWebSocketCallback
        {
            private WcfWebSocketClient wcfWebSocketClient;

            public ClientCallback(WcfWebSocketClient wcfWebSocketClient)
            {
                this.wcfWebSocketClient = wcfWebSocketClient;
            }
            public Task OnMessage(Message message)
            {
                return Task.Factory.StartNew(() =>
                    {
                        var bytes = message.GetBody<byte[]>();
                        var str = Encoding.UTF8.GetString(bytes);

                        if (wcfWebSocketClient.OnMessage != null)
                            wcfWebSocketClient.OnMessage(str);
                    });
            }
        }

        [ServiceContract(CallbackContract = typeof(IWebSocketCallback))]
        interface IWebSocket : IWebSocketCallback
        {
            [OperationContract(Action = WebSocketSettings.WebSocketConnectionOpened, IsOneWay = true)]
            void OnOpen(Message message);
        }

        [ServiceContract(CallbackContract = typeof(IWebSocketCallback))]
        interface IWebSocketCallback
        {
            [OperationContract(IsOneWay = true, Action = "*")]
            Task OnMessage(Message message);
        }
    }




}
