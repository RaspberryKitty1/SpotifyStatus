using System;
using WebSocketSharp;

namespace SpotifyStatus.TestClient
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var ws = new WebSocket("ws://127.0.0.1:1011/neos-spotify-bridge");
            ws.OnMessage += ws_OnMessage;
            ws.Connect();

            Console.ReadLine();
        }

        private static void ws_OnMessage(object sender, MessageEventArgs e)
        {
            Console.WriteLine(e.Data);
        }
    }
}