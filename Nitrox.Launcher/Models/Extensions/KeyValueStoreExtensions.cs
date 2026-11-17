using Nitrox.Model.Constants;
using Nitrox.Model.Helper;

namespace Nitrox.Launcher.Models.Extensions;

public static class KeyValueStoreExtensions
{
    extension(IKeyValueStore self)
    {
        public string GetLaunchArguments(GameInfo gameInfo, string defaultValue = "-vrmode none") => self.GetValue($"{gameInfo.Name}LaunchArguments", defaultValue);
        public void SetLaunchArguments(GameInfo gameInfo, string value) => self.SetValue($"{gameInfo.Name}LaunchArguments", value);
        public bool GetIsLightModeEnabled(bool defaultValue = false) => self.GetValue("IsLightModeEnabled", defaultValue);
        public void SetIsLightModeEnabled(bool value) => self.SetValue("IsLightModeEnabled", value);
        public bool GetIsMultipleGameInstancesAllowed(bool defaultValue = false) => self.GetValue("IsMultipleGameInstancesAllowed", defaultValue);
        public void SetIsMultipleGameInstancesAllowed(bool value) => self.SetValue("IsMultipleGameInstancesAllowed", value);
        public bool GetPreferEmbedded(bool defaultValue = true) => self.GetValue("PreferEmbedded", defaultValue);
        public void SetPreferEmbedded(bool value) => self.SetValue("PreferEmbedded", value);
        public bool GetUseBigPictureMode(bool defaultValue = false) => self.GetValue("UseBigPictureMode", defaultValue);
        public void SetBigPictureMode(bool value) => self.SetValue("UseBigPictureMode", value);
        public bool GetIsDiscordEnabled(bool defaultValue = false) => self.GetValue(DiscordConstants.ENABLE_CONFIG_KEY, defaultValue);
        public void SetIsDiscordEnabled(bool value) => self.SetValue(DiscordConstants.ENABLE_CONFIG_KEY, value);
        public bool GetLaunchDirectlyWithoutSteam(bool defaultValue = true) => self.GetValue("LaunchDirectlyWithoutSteam", defaultValue);
        public void SetLaunchDirectlyWithoutSteam(bool value) => self.SetValue("LaunchDirectlyWithoutSteam", value);
        public bool GetUseWine(bool defaultValue = true) => self.GetValue("UseWineOnLinux", defaultValue);
        public void SetUseWine(bool value) => self.SetValue("UseWineOnLinux", value);
        public string GetWineExecutable() => self.GetValue("WineExecutable", "");
        public void SetWineExecutable(string value) => self.SetValue("WineExecutable", value ?? "");
        public string GetWinePrefix() => self.GetValue("WinePrefix", "");
        public void SetWinePrefix(string value) => self.SetValue("WinePrefix", value ?? "");
    }
}
