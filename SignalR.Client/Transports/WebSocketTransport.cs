using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SignalR.Client.Transports
{
    public class WebSocketTransport : IClientTransport
    {
        private readonly object _lockObj = new object();
        private WcfWebSocketClient _client;

        public void Start(Connection connection, string data)
        {
            string url = connection.Url;

            if (string.IsNullOrEmpty(data) == false)
            {
                url += "?connectionData=" + data + "&transport=webSockets&clientId=" + connection.ClientId;
            }
            else
            {
                url += "?transport=webSockets&clientId=" + connection.ClientId;
            }

            lock (_lockObj)
            {
                _client = new WcfWebSocketClient(new Uri(url));                
                _client.OnMessage = response => ProcessResponse(connection, response);
                _client.Open().Wait();
            }
        }

        public Task<T> Send<T>(Connection connection, string data)
        {
            return _client.Send(data).ContinueWith<T>(previous => default(T));
        }

        public void Stop(Connection connection)
        {
            if (_client != null)
            {
                lock (_lockObj)
                {
                    if (_client != null)
                    {
                        _client.Dispose();
                        _client = null;
                    }
                }
            }
        }

        private static void ProcessResponse(Connection connection, string response)
        {
            if (connection.MessageId == null)
            {
                connection.MessageId = 0;
            }

            try
            {
                var result = JValue.Parse(response);

                if (!result.HasValues)
                {
                    return;
                }

                var messages = result["Messages"] as JArray;

                if (messages != null)
                {
                    foreach (var message in messages)
                    {
                        try
                        {
                            connection.OnReceived(message.ToString());
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Failed to process message: {0}", ex);
                            connection.OnError(ex);
                        }
                    }

                    connection.MessageId = result["MessageId"].Value<long>();

                    var transportData = result["TransportData"] as JObject;

                    if (transportData != null)
                    {
                        var groups = (JArray)transportData["Groups"];
                        if (groups != null)
                        {
                            connection.Groups = groups.Select(token => token.Value<string>());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to response: {0}", ex);
                connection.OnError(ex);
            }
        }
    }
}
