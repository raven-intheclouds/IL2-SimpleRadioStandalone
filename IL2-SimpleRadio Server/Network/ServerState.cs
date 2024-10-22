﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Ciribob.IL2.SimpleRadio.Standalone.Common;
using Ciribob.IL2.SimpleRadio.Standalone.Common.Helpers;
using Ciribob.IL2.SimpleRadio.Standalone.Common.Network;
using Ciribob.IL2.SimpleRadio.Standalone.Common.Setting;
using Ciribob.IL2.SimpleRadio.Standalone.Server.Settings;
using Newtonsoft.Json;
using NLog;
using LogManager = NLog.LogManager;

namespace Ciribob.IL2.SimpleRadio.Standalone.Server.Network
{
    public class ServerState : IHandle<StartServerMessage>, IHandle<StopServerMessage>, IHandle<KickClientMessage>,
        IHandle<BanClientMessage>
    {
        private static readonly string DEFAULT_CLIENT_EXPORT_FILE = "clients-list.json";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly HashSet<IPAddress> _bannedIps = new HashSet<IPAddress>();

        private readonly ConcurrentDictionary<string, SRClient> _connectedClients =
            new ConcurrentDictionary<string, SRClient>();

        private readonly IEventAggregator _eventAggregator;
        private UDPVoiceRouter _serverListener;
        private ServerSync _serverSync;
        private volatile bool _stop = true;

        public ServerState(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);

            StartServer();
        }

        public void Handle(BanClientMessage message)
        {
            WriteBanIP(message.Client);

            KickClient(message.Client);
        }

        public void Handle(KickClientMessage message)
        {
            var client = message.Client;
            KickClient(client);
        }

        public void Handle(StartServerMessage message)
        {
            StartServer();
            _eventAggregator.PublishOnUIThread(new ServerStateMessage(true,
                new List<SRClient>(_connectedClients.Values)));
        }

        public void Handle(StopServerMessage message)
        {
            StopServer();
            _eventAggregator.PublishOnUIThread(new ServerStateMessage(false,
                new List<SRClient>(_connectedClients.Values)));
        }

        private void StartExport()
        {
            _stop = false;

            string exportFilePath = ServerSettingsStore.Instance
                .GetServerSetting(ServerSettingsKeys.CLIENT_EXPORT_FILE_PATH).StringValue;
            if (string.IsNullOrWhiteSpace(exportFilePath) || exportFilePath == DEFAULT_CLIENT_EXPORT_FILE)
            {
                // Make sure we're using a full file path in case we're falling back to default values
                exportFilePath = Path.Combine(GetCurrentDirectory(), DEFAULT_CLIENT_EXPORT_FILE);
            }
            else
            {
                // Normalize file path read from config to ensure properly escaped local path
                exportFilePath = NormalizePath(exportFilePath);
            }

            string exportFileDirectory = Path.GetDirectoryName(exportFilePath);

            if (!Directory.Exists(exportFileDirectory))
            {
                Logger.Warn($"Client export directory \"{exportFileDirectory}\" does not exist, trying to create it");

                try
                {
                    Directory.CreateDirectory(exportFileDirectory);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex,
                        $"Failed to create client export directory \"{exportFileDirectory}\", falling back to default path");

                    // Failed to create desired client export directory, fall back to default path in current application directory
                    exportFilePath = NormalizePath(Path.Combine(GetCurrentDirectory(), DEFAULT_CLIENT_EXPORT_FILE));
                }
            }

            Task.Factory.StartNew(() =>
            {
                while (!_stop)
                {
                    if (ServerSettingsStore.Instance.GetGeneralSetting(ServerSettingsKeys.CLIENT_EXPORT_ENABLED)
                        .BoolValue)
                    {
                        ClientListExport data = new ClientListExport
                            {Clients = _connectedClients.Values, ServerVersion = UpdaterChecker.VERSION};
                        var json = JsonConvert.SerializeObject(data,
                            new JsonSerializerSettings
                                {ContractResolver = new JsonNetworkPropertiesResolver()}) + "\n";
                        try
                        {
                            File.WriteAllText(exportFilePath, json);
                        }
                        catch (IOException e)
                        {
                            Logger.Error(e);
                        }

                    }

                    Thread.Sleep(5000);
                }
            });
        }

        private void PopulateBanList()
        {
            try
            {
                _bannedIps.Clear();
                var lines = File.ReadAllLines(GetCurrentDirectory() + "\\banned.txt");

                foreach (var line in lines)
                {
                    IPAddress ip = null;
                    if (IPAddress.TryParse(line.Trim(), out ip))
                    {
                        Logger.Info("Loaded Banned IP: " + line);
                        _bannedIps.Add(ip);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to read banned.txt");
            }
        }

        private static string GetCurrentDirectory()
        {
            //To get the location the assembly normally resides on disk or the install directory
            var currentPath = Assembly.GetExecutingAssembly().CodeBase;

            //once you have the path you get the directory with:
            var currentDirectory = Path.GetDirectoryName(currentPath);

            if (currentDirectory.StartsWith("file:\\"))
            {
                currentDirectory = currentDirectory.Replace("file:\\", "");
            }

            return currentDirectory;
        }

        private static string NormalizePath(string path)
        {
            // Taken from https://stackoverflow.com/a/21058121 on 2018-06-22
            return Path.GetFullPath(new Uri(path).LocalPath)
               .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void StartServer()
        {
            if (_serverListener == null)
            {
                StartExport();

                PopulateBanList();

                _serverListener = new UDPVoiceRouter(_connectedClients, _eventAggregator);
                var listenerThread = new Thread(_serverListener.Listen);
                listenerThread.Start();

                _serverSync = new ServerSync(_connectedClients, _bannedIps, _eventAggregator);
                var serverSyncThread = new Thread(_serverSync.StartListening);
                serverSyncThread.Start();
            }
        }

        public void StopServer()
        {
            if (_serverListener != null)
            {
                _stop = true;
                _serverSync.RequestStop();
                _serverSync = null;
                _serverListener.RequestStop();
                _serverListener = null;
            }
        }

        private void KickClient(SRClient client)
        {
            if (client != null)
            {
                try
                {
                    ((SRSClientSession)client.ClientSession).Disconnect();
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Error kicking client");
                }
            }
        }

        private void WriteBanIP(SRClient client)
        {
            try
            {
                var remoteIpEndPoint = ((SRSClientSession)client.ClientSession).Socket.RemoteEndPoint as IPEndPoint;

                _bannedIps.Add(remoteIpEndPoint.Address);

                File.AppendAllText(GetCurrentDirectory() + "\\banned.txt",
                    remoteIpEndPoint.Address + "\r\n");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error saving banned client");
            }
        }
    }
}