using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Mix.Audio;

sealed class DeviceNotifications(Action changed) : IMMNotificationClient
{
    public void OnDeviceStateChanged(string id, DeviceState state) => changed();
    public void OnDeviceAdded(string id) => changed();
    public void OnDeviceRemoved(string id) => changed();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id) => changed();
    public void OnPropertyValueChanged(string id, PropertyKey key) => changed();
}
sealed class SessionEvents(Action level, Action topology) : IAudioSessionEventsHandler
{
    public void OnVolumeChanged(float volume, bool mute) => level();
    public void OnDisplayNameChanged(string name) => level();
    public void OnIconPathChanged(string path) { }
    public void OnChannelVolumeChanged(uint count, IntPtr volumes, uint index) => level();
    public void OnGroupingParamChanged(ref Guid id) { }
    public void OnStateChanged(AudioSessionState state) => topology();
    public void OnSessionDisconnected(AudioSessionDisconnectReason reason) => topology();
}
