﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using OpenTK;
using DependencyInjection;
using System.Net.Sockets;
using System.Net;
using System.Text.RegularExpressions;

namespace ManicDigger
{
    public interface IClientNetwork
    {
        void Dispose();
        void Connect(string serverAddress, int port, string username, string auth);
        void Process();
        void SendSetBlock(Vector3 position, BlockSetMode mode, byte type);
        event EventHandler<MapLoadedEventArgs> MapLoaded;
        void SendChat(string s);
        IEnumerable<string> ConnectedPlayers();
    }
    public class ClientNetworkDummy : IClientNetwork
    {
        public void Dispose()
        {
        }
        public void Connect(string serverAddress, int port, string username, string auth)
        {
        }
        public void Process()
        {
        }
        [Inject]
        public IMap map1 { get; set; }
        public void SendSetBlock(Vector3 position, BlockSetMode mode, byte type)
        {
            if (mode == BlockSetMode.Destroy)
            {
                type = (byte)TileTypeMinecraft.Empty;
            }
            map1.SetTileAndUpdate(position, type);
        }
        public event EventHandler<MapLoadedEventArgs> MapLoaded;
        [Inject]
        public IGui gui { get; set; }
        public void SendChat(string s)
        {
            if (s == "")
            {
                return;
            }
            string[] ss = s.Split(new char[] { ' ' });
            if (s.StartsWith("/"))
            {
                string cmd = ss[0].Substring(1);
                string arguments;
                if (s.IndexOf(" ") == -1)
                { arguments = ""; }
                else
                { arguments = s.Substring(s.IndexOf(" ")); }
                arguments = arguments.Trim();
                if (cmd == "generate")
                {
                    DoGenerate(arguments, false);
                    gui.DrawMap();
                }
            }
            gui.AddChatline(s);
        }
        [Inject]
        public IMapStorage map { get; set; }
        [Inject]
        public IGameData data { get; set; }
        [Inject]
        public fCraft.MapGenerator gen { get; set; }
        void DoGenerate(string mode, bool hollow)
        {
            switch (mode)
            {
                case "flatgrass":
                    bool reportedProgress = false;
                    playerMessage("Generating flatgrass map...");
                    for (int i = 0; i < map.MapSizeX; i++)
                    {
                        for (int j = 0; j < map.MapSizeY; j++)
                        {
                            for (int k = 1; k < map.MapSizeZ / 2 - 1; k++)
                            {
                                if (!hollow) map.SetBlock(i, j, k, data.TileIdDirt);
                            }
                            map.SetBlock(i, j, map.MapSizeZ / 2 - 1, data.TileIdGrass);
                        }
                        if (i > map.MapSizeX / 2 && !reportedProgress)
                        {
                            reportedProgress = true;
                            playerMessage("Map generation: 50%");
                        }
                    }

                    //map.MakeFloodBarrier();

                    //if (map.Save(filename))
                    //{
                    //    player.Message("Map generation: Done.");
                    //}
                    //else
                    //{
                    //    player.Message(Color.Red, "An error occured while generating the map.");
                    //}
                    break;

                case "empty":
                    playerMessage("Generating empty map...");
                    //map.MakeFloodBarrier();

                    //if (map.Save(filename))
                    //{
                    //    player.Message("Map generation: Done.");
                    //}
                    //else
                    //{
                    //    player.Message(Color.Red, "An error occured while generating the map.");
                    //}

                    break;

                case "hills":
                    playerMessage("Generating terrain...");
                    gen.GenerateMap(new fCraft.MapGeneratorParameters(
                                                                              5, 1, 0.5, 0.45, 0, 0.5, hollow));
                    break;

                case "mountains":
                    playerMessage("Generating terrain...");
                    gen.GenerateMap(new fCraft.MapGeneratorParameters(
                                                                              8, 1, 0.5, 0.45, 0.1, 0.5, hollow));
                    break;

                case "lake":
                    playerMessage("Generating terrain...");
                    gen.GenerateMap(new fCraft.MapGeneratorParameters(
                                                                              1, 0.6, 0.9, 0.45, -0.35, 0.55, hollow));
                    break;

                case "island":
                    playerMessage("Generating terrain...");
                    gen.GenerateMap(new fCraft.MapGeneratorParameters(1, 0.6, 1, 0.45, 0.3, 0.35, hollow));
                    break;

                default:
                    playerMessage("Unknown map generation mode: " + mode);
                    break;
            }
        }
        private void playerMessage(string p)
        {
            gui.AddChatline(p);
        }
        public IEnumerable<string> ConnectedPlayers()
        {
            yield return "[local player]";
        }
    }
    public class MapLoadedEventArgs : EventArgs
    {
        public byte[, ,] map;
    }
    public class ClientNetworkMinecraft : IClientNetwork
    {
        [Inject]
        public IMap map { get; set; }
        [Inject]
        public IPlayers players { get; set; }
        //public void Connect(LoginData login, string username)
        public void Connect(string serverAddress, int port, string username, string auth)
        {
            main = new Socket(AddressFamily.InterNetwork,
                   SocketType.Stream, ProtocolType.Tcp);

            iep = new IPEndPoint(IPAddress.Any, port);
            main.Connect(serverAddress, port);
            byte[] n = CreateLoginPacket(username, auth);
            main.Send(n);
        }
        private static byte[] CreateLoginPacket(string username, string verificationKey)
        {
            MemoryStream n = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(n);
            bw.Write((byte)0);//Packet ID 
            bw.Write((byte)0x07);//Protocol version
            bw.Write(StringToBytes(username));//Username
            bw.Write(StringToBytes(verificationKey));//Verification key
            bw.Write((byte)0);//Unused
            return n.ToArray();
        }
        IPEndPoint iep;
        Socket main;
        public void SendPacket(byte[] packet)
        {
            int sent = main.Send(packet);
            if (sent != packet.Length)
            {
                throw new Exception();
            }
        }
        public void Disconnect()
        {
            ChatLog("---Disconnected---");
            main.Disconnect(false);
        }
        [Inject]
        public ILocalPlayerPosition position { get; set; }
        DateTime lastpositionsent;
        public void SendSetBlock(Vector3 position, BlockSetMode mode, byte type)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);
            bw.Write((byte)ClientPacketId.SetBlock);
            WriteInt16(bw, (short)(position.X));//-4
            WriteInt16(bw, (short)(position.Z));
            WriteInt16(bw, (short)position.Y);
            bw.Write((byte)(mode == BlockSetMode.Create ? 1 : 0));
            bw.Write((byte)type);
            SendPacket(ms.ToArray());
        }
        public void SendChat(string s)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);
            bw.Write((byte)ClientPacketId.Message);
            bw.Write((byte)255);//unused
            WriteString64(bw, s);
            SendPacket(ms.ToArray());
        }
        public void Process()
        {
            if (main == null)
            {
                return;
            }
            for (; ; )
            {
                if (!main.Poll(0, SelectMode.SelectRead))
                {
                    break;
                }
                byte[] data = new byte[1024];
                int recv;
                try
                {
                    recv = main.Receive(data);
                }
                catch
                {
                    recv = 0;
                }
                if (recv == 0)
                {
                    //disconnected
                    return;
                }
                for (int i = 0; i < recv; i++)
                {
                    received.Add(data[i]);
                }
                for (; ; )
                {
                    if (received.Count < 4)
                    {
                        break;
                    }
                    byte[] packet = new byte[received.Count];
                    int bytesRead;
                    bytesRead = TryReadPacket();
                    if (bytesRead > 0)
                    {
                        received.RemoveRange(0, bytesRead);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            if (spawned && ((DateTime.Now - lastpositionsent).TotalSeconds > 0.1))
            {
                lastpositionsent = DateTime.Now;
                MemoryStream ms = new MemoryStream();
                BinaryWriter bw = new BinaryWriter(ms);
                bw.Write((byte)ClientPacketId.PositionandOrientation);
                bw.Write((byte)255);//player id, self
                WriteInt16(bw, (short)((position.LocalPlayerPosition.X) * 32));//gfd1
                WriteInt16(bw, (short)((position.LocalPlayerPosition.Y + CharacterPhysics.characterheight) * 32));
                WriteInt16(bw, (short)(position.LocalPlayerPosition.Z * 32));
                bw.Write((byte)((((position.LocalPlayerOrientation.Y) % (2 * Math.PI)) / (2 * Math.PI)) * 256));
                bw.Write(PitchByte());
                SendPacket(ms.ToArray());
            }
        }
        private byte PitchByte()
        {
            double xx = (position.LocalPlayerOrientation.X + Math.PI) % (2 * Math.PI);
            xx = xx / (2 * Math.PI);
            return (byte)(xx * 256);
        }
        bool spawned = false;
        string ServerName;
        private int TryReadPacket()
        {
            BinaryReader br = new BinaryReader(new MemoryStream(received.ToArray()));
            if (received.Count == 0)
            {
                return 0;
            }
            var packetId = (ServerPacketId)br.ReadByte();
            int totalread = 1;
            if (packetId != ServerPacketId.PositionandOrientationUpdate
                 && packetId != ServerPacketId.PositionUpdate
                && packetId != ServerPacketId.OrientationUpdate
                && packetId != ServerPacketId.PlayerTeleport)
            {
                Console.WriteLine(Enum.GetName(typeof(ServerPacketId), packetId));
            }
            if (packetId == ServerPacketId.ServerIdentification)
            {
                totalread += 1 + 64 + 64 + 1; if (received.Count < totalread) { return 0; }
                ServerPlayerIdentification p = new ServerPlayerIdentification();
                p.ProtocolVersion = br.ReadByte();
                if (p.ProtocolVersion != 7)
                {
                    throw new Exception();
                }
                p.ServerName = ReadString64(br);
                p.ServerMotd = ReadString64(br);
                p.UserType = br.ReadByte();
                //connected = true;
                this.ServerName = p.ServerName;
                ChatLog("---Connected---");
            }
            else if (packetId == ServerPacketId.Ping)
            {
            }
            else if (packetId == ServerPacketId.LevelInitialize)
            {
                receivedMapStream = new MemoryStream();
            }
            else if (packetId == ServerPacketId.LevelDataChunk)
            {
                totalread += 2 + 1024 + 1; if (received.Count < totalread) { return 0; }
                int chunkLength = ReadInt16(br);
                byte[] chunkData = br.ReadBytes(1024);
                BinaryWriter bw1 = new BinaryWriter(receivedMapStream);
                byte[] chunkDataWithoutPadding = new byte[chunkLength];
                for (int i = 0; i < chunkLength; i++)
                {
                    chunkDataWithoutPadding[i] = chunkData[i];
                }
                bw1.Write(chunkDataWithoutPadding);
                MapLoadingPercentComplete = br.ReadByte();
                Console.WriteLine(MapLoadingPercentComplete);
            }
            else if (packetId == ServerPacketId.LevelFinalize)
            {
                totalread += 2 + 2 + 2; if (received.Count < totalread) { return 0; }
                mapreceivedsizex = ReadInt16(br);
                mapreceivedsizez = ReadInt16(br);
                mapreceivedsizey = ReadInt16(br);
                receivedMapStream.Seek(0, SeekOrigin.Begin);
                MemoryStream decompressed = new MemoryStream(GzipCompression.Decompress(receivedMapStream.ToArray()));
                if (decompressed.Length != mapreceivedsizex * mapreceivedsizey * mapreceivedsizez +
                    (decompressed.Length % 1024))
                {
                    //throw new Exception();
                    Console.WriteLine("warning: invalid map data size");
                }
                byte[, ,] receivedmap = new byte[mapreceivedsizex, mapreceivedsizey, mapreceivedsizez];
                {
                    BinaryReader br2 = new BinaryReader(decompressed);
                    int wtf1 = br2.ReadByte();
                    int wtf2 = br2.ReadByte();
                    int wtf3 = br2.ReadByte();
                    int wtf4 = br2.ReadByte();
                    for (int z = 0; z < mapreceivedsizez; z++)
                    {
                        for (int y = 0; y < mapreceivedsizey; y++)
                        {
                            for (int x = 0; x < mapreceivedsizex; x++)
                            {
                                receivedmap[x, y, z] = br2.ReadByte();
                            }
                        }
                    }
                }
                if (MapLoaded != null)
                {
                    MapLoaded.Invoke(this, new MapLoadedEventArgs() { map = receivedmap });
                }
            }
            else if (packetId == ServerPacketId.SetBlock)
            {
                totalread += 2 + 2 + 2 + 1; if (received.Count < totalread) { return 0; }
                int x = ReadInt16(br);
                int z = ReadInt16(br);
                int y = ReadInt16(br);
                byte type = br.ReadByte();
                map.SetTileAndUpdate(new Vector3(x, y, z), type);
            }
            else if (packetId == ServerPacketId.SpawnPlayer)
            {
                totalread += 1 + 64 + 2 + 2 + 2 + 1 + 1; if (received.Count < totalread) { return 0; }
                byte playerid = br.ReadByte();
                string playername = ReadString64(br);
                connectedplayers.Add(new ConnectedPlayer() { name = playername, id = playerid });
                if (players.Players.ContainsKey(playerid))
                {
                    //throw new Exception();
                }
                players.Players[playerid] = new Player();
                ReadAndUpdatePlayerPosition(br, playerid);
            }
            else if (packetId == ServerPacketId.PlayerTeleport)
            {
                totalread += 1 + (2 + 2 + 2) + 1 + 1; if (received.Count < totalread) { return 0; }
                byte playerid = br.ReadByte();
                ReadAndUpdatePlayerPosition(br, playerid);
            }
            else if (packetId == ServerPacketId.PositionandOrientationUpdate)
            {
                totalread += 1 + (1 + 1 + 1) + 1 + 1; if (received.Count < totalread) { return 0; }
                byte playerid = br.ReadByte();
                float x = (float)br.ReadByte() / 32;
                float y = (float)br.ReadByte() / 32;
                float z = (float)br.ReadByte() / 32;
                byte heading = br.ReadByte();
                byte pitch = br.ReadByte();
                Vector3 v = new Vector3(x, y, z);
                UpdatePositionDiff(playerid, v);
            }
            else if (packetId == ServerPacketId.PositionUpdate)
            {
                totalread += 1 + 1 + 1 + 1; if (received.Count < totalread) { return 0; }
                byte playerid = br.ReadByte();
                float x = (float)br.ReadByte() / 32;
                float y = (float)br.ReadByte() / 32;
                float z = (float)br.ReadByte() / 32;
                Vector3 v = new Vector3(x, y, z);
                UpdatePositionDiff(playerid, v);
            }
            else if (packetId == ServerPacketId.OrientationUpdate)
            {
                totalread += 1 + 1 + 1; if (received.Count < totalread) { return 0; }
            }
            else if (packetId == ServerPacketId.DespawnPlayer)
            {
                totalread += 1; if (received.Count < totalread) { return 0; }
                byte playerid = br.ReadByte();
                for (int i = 0; i < connectedplayers.Count; i++)
                {
                    if (connectedplayers[i].id == playerid)
                    {
                        connectedplayers.RemoveAt(i);
                    }
                }
                players.Players.Remove(playerid);
            }
            else if (packetId == ServerPacketId.Message)
            {
                totalread += 1 + 64; if (received.Count < totalread) { return 0; }
                byte unused = br.ReadByte();
                string message = ReadString64(br);
                chatlines.AddChatline(message);
                ChatLog(message);
            }
            else if (packetId == ServerPacketId.DisconnectPlayer)
            {
                totalread += 64; if (received.Count < totalread) { return 0; }
                string disconnectReason = ReadString64(br);
                throw new Exception(disconnectReason);
            }
            else
            {
                throw new Exception();
            }
            return totalread;
        }
        public bool ENABLE_CHATLOG = true;
        private void ChatLog(string p)
        {
            if (!ENABLE_CHATLOG)
            {
                return;
            }
            string logsdir = "logs";
            if (!Directory.Exists(logsdir))
            {
                Directory.CreateDirectory(logsdir);
            }
            string filename=Path.Combine(logsdir, MakeValidFileName(ServerName) + ".txt");
            try
            {
                File.AppendAllText(filename, string.Format("{0} {1}\n", DateTime.Now, p));
            }
            catch
            {
                Console.WriteLine("Cannot write to chat log file {0}.", filename);
            }
        }
        private static string MakeValidFileName(string name)
        {
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidReStr = string.Format(@"[{0}]", invalidChars);
            return Regex.Replace(name, invalidReStr, "_");
        }
        private void UpdatePositionDiff(byte playerid, Vector3 v)
        {
            if (playerid == 255)
            {
                position.LocalPlayerPosition += v;
                spawned = true;
            }
            else
            {
                if (!players.Players.ContainsKey(playerid))
                {
                    players.Players[playerid] = new Player();
                    //throw new Exception();
                    Console.WriteLine("Position update of nonexistent player {0}." + playerid);
                }
                players.Players[playerid].Position += v;
            }
        }
        private void ReadAndUpdatePlayerPosition(BinaryReader br, byte playerid)
        {
            float x = (float)ReadInt16(br) / 32;
            float y = (float)ReadInt16(br) / 32;
            float z = (float)ReadInt16(br) / 32;
            byte heading = br.ReadByte();
            byte pitch = br.ReadByte();
            Vector3 realpos = new Vector3(x, y, z) + new Vector3(0.5f, 0, 0.5f);
            if (playerid == 255)
            {
                position.LocalPlayerPosition = realpos;
                spawned = true;
            }
            else
            {
                if (!players.Players.ContainsKey(playerid))
                {
                    players.Players[playerid] = new Player();
                }
                players.Players[playerid].Position = realpos;
            }
        }
        [Inject]
        public IGui chatlines { get; set; }
        List<byte> received = new List<byte>();
        public void Dispose()
        {
            if (main != null)
            {
                //main.DisconnectAsync(new SocketAsyncEventArgs());
                main.Disconnect(false);
                main = null;
            }
            //throw new NotImplementedException();
        }
        enum ClientPacketId
        {
            PlayerIdentification = 0,
            SetBlock = 5,
            PositionandOrientation = 8,
            Message = 0x0d,
        }
        enum ServerPacketId
        {
            ServerIdentification = 0,
            Ping = 1,
            LevelInitialize = 2,
            LevelDataChunk = 3,
            LevelFinalize = 4,
            SetBlock = 6,
            SpawnPlayer = 7,
            PlayerTeleport = 8,
            PositionandOrientationUpdate = 9,
            PositionUpdate = 10,
            OrientationUpdate = 11,
            DespawnPlayer = 12,
            Message = 13,
            DisconnectPlayer = 14,
        }
        private static byte[] StringToBytes(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            byte[] bb = new byte[64];
            for (int i = 0; i < bb.Length; i++)
            {
                bb[i] = 32; //' '
            }
            for (int i = 0; i < b.Length; i++)
            {
                bb[i] = b[i];
            }
            return bb;
        }
        private static string BytesToString(byte[] s)
        {
            string b = Encoding.ASCII.GetString(s).Trim();
            return b;
        }
        public int mapreceivedsizex;
        public int mapreceivedsizey;
        public int mapreceivedsizez;
        int ReadInt16(BinaryReader br)
        {
            byte[] array = br.ReadBytes(2);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(array);
            }
            return BitConverter.ToInt16(array, 0);
        }
        void WriteInt16(BinaryWriter bw, short v)
        {
            byte[] array = BitConverter.GetBytes((short)v);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(array);
            }
            bw.Write(array);
        }
        int MapLoadingPercentComplete;
        public MemoryStream receivedMapStream;
        static string ReadString64(BinaryReader br)
        {
            return BytesToString(br.ReadBytes(64));
        }
        static void WriteString64(BinaryWriter bw, string s)
        {
            bw.Write(StringToBytes(s));
        }
        struct ServerPlayerIdentification
        {
            public byte ProtocolVersion;
            public string ServerName;
            public string ServerMotd;
            public byte UserType;
        }
        public event EventHandler<MapLoadedEventArgs> MapLoaded;
        class ConnectedPlayer
        {
            public int id;
            public string name;
        }
        List<ConnectedPlayer> connectedplayers = new List<ConnectedPlayer>();
        public IEnumerable<string> ConnectedPlayers()
        {
            foreach (ConnectedPlayer p in connectedplayers)
            {
                yield return p.name;
            }
        }
    }
    public class LoginData
    {
        public string serveraddress;
        public int port;
        public string mppass;
    }
    public class LoginClientMinecraft
    {
        public LoginData Login(string username, string password, string gameurl)
        {
            //Three Steps

            //Step 1.
            //---
            //Go to http://www.minecraft.net/login.jsp and GET, you will receive JSESSIONID cookie.
            //---
            string loginurl = "http://www.minecraft.net/login.jsp";
            string data11 = string.Format("username={0}&password={1}", username, password);
            string sessionidcookie;
            string sessionid;
            {
                using (WebClient c = new WebClient())
                {
                    string html = c.DownloadString(loginurl);
                    sessionidcookie = c.ResponseHeaders[HttpResponseHeader.SetCookie];
                    sessionid = sessionidcookie.Substring(0, sessionidcookie.IndexOf(";"));
                    sessionid = sessionid.Substring(sessionid.IndexOf("=") + 1);
                }
            }
            //Step 2.
            //---
            //Go to http://www.minecraft.net/login.jsp and POST "username={0}&password={1}" using JSESSIONID cookie.
            //You will receive logged in cookie ("_uid").
            //Because of multipart http page, HttpWebRequest has some trouble receiving cookies in step 2,
            //so it is easier to just use raw TcpClient for this.
            //---
            List<string> loggedincookie = new List<string>();
            {
                using (TcpClient step2Client = new TcpClient("minecraft.net", 80))
                {
                    var stream = step2Client.GetStream();
                    StreamWriter sw = new StreamWriter(stream);

                    sw.WriteLine("POST /login.jsp HTTP/1.0");
                    sw.WriteLine("Host: www.minecraft.net");
                    sw.WriteLine("Content-Type: application/x-www-form-urlencoded");
                    sw.WriteLine("Set-Cookie: " + sessionidcookie);
                    sw.WriteLine("Content-Length: " + data11.Length);
                    sw.WriteLine("");
                    sw.WriteLine(data11);

                    sw.Flush();
                    StreamReader sr = new StreamReader(stream);
                    for (; ; )
                    {
                        var s = sr.ReadLine();
                        if (s == null)
                        {
                            break;
                        }
                        if (s.Contains("Set-Cookie"))
                        {
                            loggedincookie.Add(s);
                        }
                    }
                }
            }
            for (int i = 0; i < loggedincookie.Count; i++)
            {
                loggedincookie[i] = loggedincookie[i].Replace("Set-", "");
            }
            //Step 3.
            //---
            //Go to game url and GET using JSESSIONID cookie and _uid cookie.
            //Parse the page to find server, port, mpass strings.
            //---
            WebRequest step3Request = (HttpWebRequest)HttpWebRequest.Create(gameurl);
            foreach (string cookie in loggedincookie)
            {
                step3Request.Headers.Add(cookie);
            }
            using (var s4 = step3Request.GetResponse().GetResponseStream())
            {
                string html = new StreamReader(s4).ReadToEnd();
                string serveraddress = ReadValue(html.Substring(html.IndexOf("\"server\""), 40));
                string port = ReadValue(html.Substring(html.IndexOf("\"port\""), 40));
                string mppass = ReadValue(html.Substring(html.IndexOf("\"mppass\""), 80));
                return new LoginData() { serveraddress = serveraddress, port = int.Parse(port), mppass = mppass };
            }
        }
        private static string ReadValue(string s)
        {
            string start = "value=\"";
            string end = "\"";
            string ss = s.Substring(s.IndexOf(start) + start.Length);
            ss = ss.Substring(0, ss.IndexOf(end));
            return ss;
        }
    }
}