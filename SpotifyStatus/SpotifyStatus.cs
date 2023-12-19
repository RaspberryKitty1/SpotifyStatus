using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BaseX;
using CodeX;
using FrooxEngine;
using FrooxEngine.LogiX;
using FrooxEngine.LogiX.Data;
using FrooxEngine.LogiX.ProgramFlow;
using FrooxEngine.UIX;
using HarmonyLib;
using NeosModLoader;

namespace SpotifyStatus
{
    public class SpotifyStatus : NeosMod
    {
        public static ModConfiguration Config;

        [AutoRegisterConfigKey]
        internal static ModConfigurationKey<string> ClientId = new ModConfigurationKey<string>("ClientId", "Your Spotify API Application's Client ID.", () => "");

        [AutoRegisterConfigKey]
        internal static ModConfigurationKey<string> ClientSecret = new ModConfigurationKey<string>("ClientSecret", "Your Spotify API Application's Client Secret.", () => "");

        [AutoRegisterConfigKey]
        internal static ModConfigurationKey<bool> Enable = new ModConfigurationKey<bool>("Enable", "Enable Spotify WebSocket.", () => false);

        [AutoRegisterConfigKey]
        internal static ModConfigurationKey<int> Port = new ModConfigurationKey<int>("Port", "Port to host the WebSocket on.", () => 1011);

        [AutoRegisterConfigKey]
        internal static ModConfigurationKey<string> RefreshToken = new ModConfigurationKey<string>("RefreshToken", "The current refresh token. Don't change.", () => "");

        public override string Author => "Banane9";
        public override string Link => "https://github.com/Banane9/SpotifyStatus";
        public override string Name => "SpotifyStatus";
        public override string Version => "1.0.0";

        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony($"{Author}.{Name}");
            Config = GetConfiguration();
            Config.Save(true);
            Config.OnThisConfigurationChanged += config_OnThisConfigurationChanged;

            if (Config.GetValue(Enable))
                SpotifyTracker.Start();
        }

        private void config_OnThisConfigurationChanged(ConfigurationChangedEvent configurationChangedEvent)
        {
            if ((configurationChangedEvent.Key == RefreshToken && string.IsNullOrWhiteSpace(Config.GetValue(RefreshToken)))
              || configurationChangedEvent.Key == ClientId || configurationChangedEvent.Key == ClientSecret || configurationChangedEvent.Key == Port)
                Config.Set(Enable, false);

            if (configurationChangedEvent.Key == Enable)
            {
                if (Config.GetValue(Enable))
                    SpotifyTracker.Start();
                else
                    SpotifyTracker.Stop();
            }
        }
    }
}