﻿#region Using Statements
using System;
using System.Diagnostics;
using System.Threading;
using GameModeFortress;
using ManicDigger;
using ManicDigger.MapTools;
using ManicDigger.MapTools.Generators;
#endregion

namespace ManicDiggerServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Server server = new Server();
            server.LoadConfig();
            var map = new ManicDiggerServer.ServerMap();
            map.currenttime = server;
            map.chunksize = 32;

            // TODO: make it possible to change the world generator at run-time!
            var generator = new Noise3DWorldGenerator();
            generator.ChunkSize = map.chunksize;
            // apply chunk size to generator
            map.generator = generator;

            map.heightmap = new InfiniteMapChunked2d() { chunksize = 32, map = map };
            map.Reset(server.config.MapSizeX, server.config.MapSizeY, server.config.MapSizeZ);
            server.map = map;
            server.generator = generator;
            server.data = new GameDataManicDigger();
            map.data = server.data;
            server.craftingtabletool = new CraftingTableTool() { map = map };
            bool singleplayer = false;
            foreach (string arg in args)
            {
                if (arg.Equals("singleplayer", StringComparison.InvariantCultureIgnoreCase))
                {
                    singleplayer = true;
                }
            }
            server.LocalConnectionsOnly = singleplayer;
            server.getfile = new GetFilePath(new[] { "mine", "minecraft" });
            var compression = new CompressionGzip();
            var chunkdb = new ChunkDbCompressed() { chunkdb = new ChunkDbSqlite(), compression = compression };
            server.chunkdb = chunkdb;
            map.chunkdb = chunkdb;
            server.networkcompression = compression;
            server.water = new WaterFinite() { data = server.data };
            if (Debugger.IsAttached)
            {
                new DependencyChecker(typeof(InjectAttribute)).CheckDependencies(
                    server, generator, map);
            }
            server.Start();
            if ((!singleplayer) && (server.config.Public))
            {
                new Thread((a) => { for (; ; ) { server.SendHeartbeat(); Thread.Sleep(TimeSpan.FromMinutes(1)); } }).Start();
            }
            for (; ; )
            {
                server.Process();
                Thread.Sleep(1);
            }
        }
    }
}