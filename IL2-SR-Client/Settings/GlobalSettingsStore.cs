﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using Ciribob.IL2.SimpleRadio.Standalone.Client.Settings;
using Ciribob.IL2.SimpleRadio.Standalone.Common.Network;
using NLog;
using SharpConfig;

namespace Ciribob.IL2.SimpleRadio.Standalone.Client.Settings
{
  
    public enum GlobalSettingsKeys
    {
        MinimiseToTray,
        StartMinimised,

        RefocusIL2,
        ExpandControls,
        AutoConnectPrompt, //message about auto connect
        RadioOverlayTaskbarHide,

        AudioInputDeviceId,
        AudioOutputDeviceId,
        LastServer,
        MicBoost ,
        SpeakerBoost,
        RadioX ,
        RadioY ,
        RadioSize,
        RadioOpacity,
        RadioWidth ,
        RadioHeight,
        ClientX,
        ClientY,
        AwacsX,
        AwacsY,
        MicAudioOutputDeviceId,

        ClientIdLong,
        IL2LOSOutgoingUDP, //9086
        IL2IncomingUDP, //9084
        CommandListenerUDP, //=9040,
        OutgoingIL2UDPInfo, //7080

        AGC,
        AGCTarget,
        AGCDecrement,
        AGCLevelMax,

        Denoise,
        DenoiseAttenuation,

        LastSeenName,

        CheckForBetaUpdates,

        AllowMultipleInstances, // Allow for more than one SRS instance to be ran simultaneously. Config-file only!

        AutoConnectMismatchPrompt, //message about auto connect mismatch

        DisableWindowVisibilityCheck ,
        PlayConnectionSounds,

        RequireAdmin,

        SettingsProfiles,

        ShowTransmitterName
    }

    public enum InputBinding
    {
        Intercom = 100,
        ModifierIntercom = 200,

        Switch1 = 101,
        ModifierSwitch1 = 201,

        Switch2 = 102,
        ModifierSwitch2 = 202,

        Switch3 = 103,
        ModifierSwitch3 = 203,

        Switch4 = 104,
        ModifierSwitch4 = 204,

        Switch5 = 105,
        ModifierSwitch5 = 205,

        Switch6 = 106,
        ModifierSwitch6 = 206,

        Switch7 = 107,
        ModifierSwitch7 = 207,

        Switch8 = 108,
        ModifierSwitch8 = 208,

        Switch9 = 109,
        ModifierSwitch9 = 209,

        Switch10 = 110,
        ModifierSwitch10 = 210,

        Ptt = 111,
        ModifierPtt = 211,

        OverlayToggle = 112,
        ModifierOverlayToggle = 212,

        RadioChannel1 = 113,
        ModifierRadioChannel1 = 213,

        RadioChannel2 = 114,
        ModifierRadioChannel2 = 214,

        RadioChannel3 = 115,
        ModifierRadioChannel3 = 215,

        RadioChannel4 = 116,
        ModifierRadioChannel4 = 216,

        RadioChannel5 = 117,
        ModifierRadioChannel5 = 217,

        RadioChannel6 = 118,
        ModifierRadioChannel6 = 218,

        RadioChannel7 = 119,
        ModifierRadioChannel7 = 219,

        RadioChannel8 = 120,
        ModifierRadioChannel8 = 220,

        RadioChannel9 = 121,
        ModifierRadioChannel9 = 221,

        RadioChannel10 = 122,
        ModifierRadioChannel10 = 222,

        RadioChannel11 = 123,
        ModifierRadioChannel11 = 223,

        RadioChannel12 = 124,
        ModifierRadioChannel12 = 224,

        NextRadio = 125,
        ModifierNextRadio = 225,

        PreviousRadio = 126,
        ModifierPreviousRadio = 226,

        ToggleGuard = 127,
        ModifierToggleGuard = 227,

        ToggleEncryption = 128,
        ModifierToggleEncryption = 228,

        ReadStatus = 130,
        ModifierReadStatus = 230,

        RadioChannelUp = 131,
        ModifierRadioChannelUp = 231,

        RadioChannelDown = 132,
        ModifierRadioChannelDown = 232,

    }


    public class GlobalSettingsStore
    {
        private static readonly string CFG_FILE_NAME = "global.cfg";

        private static readonly string PREVIOUS_CFG_FILE_NAME = "client.cfg";

        private static readonly object _lock = new object();

        private readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly Configuration _configuration;

        public string ConfigFileName { get; } = CFG_FILE_NAME;

        private  ProfileSettingsStore _profileSettingsStore;
        public ProfileSettingsStore ProfileSettingsStore => _profileSettingsStore;

        public string Path { get; } = "";

        private GlobalSettingsStore()
        {

            //check commandline
            var args = Environment.GetCommandLineArgs();
            
            foreach (var arg in args)
            {
                if (arg.Trim().StartsWith("-cfg="))
                {
                    Path = arg.Trim().Replace("-cfg=", "").Trim();
                    if (!Path.EndsWith("\\"))
                    {
                        Path = Path + "\\";
                    }
                    Logger.Info($"Found -cfg loading: {Path +ConfigFileName}");
                }
            }

            try
            {
                _configuration = Configuration.LoadFromFile(ConfigFileName);
            }
            catch (FileNotFoundException ex)
            {
                Logger.Info($"Did not find client config file at path ${Path}/${ConfigFileName}, initialising with default config");

                _configuration = new Configuration();
                _configuration.Add(new Section("Position Settings"));
                _configuration.Add(new Section("Client Settings"));
                _configuration.Add(new Section("Network Settings"));

                Save();
            }
            catch (ParserException ex)
            {
                Logger.Error(ex, "Failed to parse client config, potentially corrupted. Creating backing and re-initialising with default config");

                MessageBox.Show("Failed to read client config, it might have become corrupted.\n" +
                    "SRS will create a backup of your current config file (client.cfg.bak) and initialise using default settings.",
                    "Config error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                try
                {
                    File.Copy(Path+ConfigFileName, Path + ConfigFileName +".bak", true);
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to create backup of corrupted config file, ignoring");
                }

                _configuration = new Configuration();
                _configuration.Add(new Section("Position Settings"));
                _configuration.Add(new Section("Client Settings"));
                _configuration.Add(new Section("Network Settings"));

                Save();
            }

            _profileSettingsStore = new ProfileSettingsStore(this);
        }

        public static GlobalSettingsStore Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GlobalSettingsStore();

                    //stops cyclic init
                    
                }
                return _instance;
            }
        }

        public void SetClientSetting(GlobalSettingsKeys key, string[] strArray)
        {
            SetSetting("Client Settings", key.ToString(), strArray);
        }

        private readonly Dictionary<string, string> defaultGlobalSettings = new Dictionary<string, string>()
        {
            {GlobalSettingsKeys.AutoConnectPrompt.ToString(), "false"},
            {GlobalSettingsKeys.AutoConnectMismatchPrompt.ToString(), "true"},
            {GlobalSettingsKeys.RadioOverlayTaskbarHide.ToString(), "false"},
            {GlobalSettingsKeys.RefocusIL2.ToString(), "false"},
            {GlobalSettingsKeys.ExpandControls.ToString(), "false"},

            {GlobalSettingsKeys.MinimiseToTray.ToString(), "false"},
            {GlobalSettingsKeys.StartMinimised.ToString(), "false"},


            {GlobalSettingsKeys.AudioInputDeviceId.ToString(), ""},
            {GlobalSettingsKeys.AudioOutputDeviceId.ToString(), ""},
            {GlobalSettingsKeys.MicAudioOutputDeviceId.ToString(), ""},

            {GlobalSettingsKeys.LastServer.ToString(), "127.0.0.1"},

            {GlobalSettingsKeys.MicBoost.ToString(), "0.514"},
            {GlobalSettingsKeys.SpeakerBoost.ToString(), "0.514"},

            {GlobalSettingsKeys.RadioX.ToString(), "300"},
            {GlobalSettingsKeys.RadioY.ToString(), "300"},
            {GlobalSettingsKeys.RadioSize.ToString(), "1.0"},
            {GlobalSettingsKeys.RadioOpacity.ToString(), "1.0"},

            {GlobalSettingsKeys.RadioWidth.ToString(), "122"},
            {GlobalSettingsKeys.RadioHeight.ToString(), "270"},

            {GlobalSettingsKeys.ClientX.ToString(), "200"},
            {GlobalSettingsKeys.ClientY.ToString(), "200"},

            {GlobalSettingsKeys.AwacsX.ToString(), "300"},
            {GlobalSettingsKeys.AwacsY.ToString(), "300"},

        //    {GlobalSettingsKeys.CliendIdShort.ToString(), ShortGuid.NewGuid().ToString()},
            {GlobalSettingsKeys.ClientIdLong.ToString(), Guid.NewGuid().ToString()},

            {GlobalSettingsKeys.IL2IncomingUDP.ToString(), "4322"},
            {GlobalSettingsKeys.CommandListenerUDP.ToString(), "4330"},
            {GlobalSettingsKeys.OutgoingIL2UDPInfo.ToString(), "4340"},


            {GlobalSettingsKeys.AGC.ToString(), "true"},
            {GlobalSettingsKeys.AGCTarget.ToString(), "30000"},
            {GlobalSettingsKeys.AGCDecrement.ToString(), "-60"},
            {GlobalSettingsKeys.AGCLevelMax.ToString(),"68" },

            {GlobalSettingsKeys.Denoise.ToString(),"true" },
            {GlobalSettingsKeys.DenoiseAttenuation.ToString(),"-30" },

            {GlobalSettingsKeys.LastSeenName.ToString(), ""},

            {GlobalSettingsKeys.CheckForBetaUpdates.ToString(), "false"},

            {GlobalSettingsKeys.AllowMultipleInstances.ToString(), "false"},

            {GlobalSettingsKeys.DisableWindowVisibilityCheck.ToString(), "true"},
            {GlobalSettingsKeys.PlayConnectionSounds.ToString(), "true"},

            {GlobalSettingsKeys.RequireAdmin.ToString(),"true" },

            {GlobalSettingsKeys.ShowTransmitterName.ToString(), "true"},

        };

        private readonly Dictionary<string, string[]> defaultArraySettings = new Dictionary<string, string[]>()
        {
            {GlobalSettingsKeys.SettingsProfiles.ToString(), new string[]{"default.cfg"} }
        };

        public Setting GetPositionSetting(GlobalSettingsKeys key)
        {
            return GetSetting("Position Settings", key.ToString());
        }

        public void SetPositionSetting(GlobalSettingsKeys key, double value)
        {
            SetSetting("Position Settings", key.ToString(), value.ToString(CultureInfo.InvariantCulture));
        }

        public bool GetClientSettingBool(GlobalSettingsKeys key)
        {
            var setting = GetSetting("Client Settings", key.ToString());
            if (setting.RawValue.Length == 0)
            {
                return false;
            }

            return setting.BoolValue;
        }

        public Setting GetClientSetting(GlobalSettingsKeys key)
        {
            return GetSetting("Client Settings", key.ToString());
        }

        public void SetClientSetting(GlobalSettingsKeys key, string value)
        {
            SetSetting("Client Settings", key.ToString(), value);
        }

        public void SetClientSetting(GlobalSettingsKeys key, bool value)
        {
            SetSetting("Client Settings", key.ToString(), value);
        }

        public int GetNetworkSetting(GlobalSettingsKeys key)
        {
            return GetSetting("Network Settings", key.ToString()).IntValue;
        }

        public void SetNetworkSetting(GlobalSettingsKeys key, int value)
        {
            SetSetting("Network Settings", key.ToString(), value.ToString(CultureInfo.InvariantCulture));
        }

        private Setting GetSetting(string section, string setting)
        {
            if (!_configuration.Contains(section))
            {
                _configuration.Add(section);
            }

            if (!_configuration[section].Contains(setting))
            {
                if (defaultGlobalSettings.ContainsKey(setting))
                {
                    //save
                    _configuration[section]
                        .Add(new Setting(setting, defaultGlobalSettings[setting]));

                    Save();
                }
                else if(defaultArraySettings.ContainsKey(setting))
                {
                    //save
                    _configuration[section]
                        .Add(new Setting(setting, defaultArraySettings[setting]));

                    Save();
                }
                else
                {
                    _configuration[section]
                        .Add(new Setting(setting, ""));
                    Save();
                }
            }

            return _configuration[section][setting];
        }

        private void SetSetting(string section, string key, object setting)
        {
            if (setting == null)
            {
                setting = "";
            }
            if (!_configuration.Contains(section))
            {
                _configuration.Add(section);
            }

            if (!_configuration[section].Contains(key))
            {
                _configuration[section].Add(new Setting(key, setting));
            }
            else
            {
                
                if (setting is bool)
                {
                    _configuration[section][key].BoolValue = (bool) setting ;
                }
                else if (setting.GetType() == typeof(string))
                {
                    _configuration[section][key].StringValue = setting as string;
                }
                else if(setting is string[])
                {
                    _configuration[section][key].StringValueArray = setting as string[];
                }
                else
                {
                    Logger.Error("Unknown Setting Type - Not Saved ");
                }
                
            }

            Save();
        }

        private static GlobalSettingsStore _instance;

        private void Save()
        {
            lock (_lock)
            {
                try
                {
                    _configuration.SaveToFile(Path + ConfigFileName);
                }
                catch (Exception ex)
                {
                    Logger.Error("Unable to save settings!");
                }
            }
        }

    }
}