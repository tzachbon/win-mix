using System;
using System.Collections.Generic;
using System.Linq;

namespace Mix.Core;

public sealed class ShortcutRecorder
{
    private const int VkEscape = 0x1B;

    private readonly HashSet<int> _down = new();
    private readonly HashSet<int> _ownedKeyUps = new();
    private readonly List<int> _captured = new();
    private int[]? _pendingCompleted;
    private int[]? _completed;
    private bool _recording;
    private bool _waitingForNeutral;
    private bool _captureStarted;
    private bool _capturedAtFirstRelease;
    private bool _cancelled;

    public bool Recording => _recording;
    public bool HasOwnedKeys => _ownedKeyUps.Count != 0;
    public int[]? Completed => _completed?.ToArray();
    public bool Cancelled => _cancelled;

    public void Start(IEnumerable<int> heldKeys)
    {
        ArgumentNullException.ThrowIfNull(heldKeys);

        _down.Clear();
        foreach (int key in heldKeys)
            _down.Add(key);
        _down.UnionWith(_ownedKeyUps);
        _captured.Clear();
        _pendingCompleted = null;
        _completed = null;
        _cancelled = false;
        _recording = true;
        _waitingForNeutral = _down.Count != 0;
        _captureStarted = false;
        _capturedAtFirstRelease = false;
    }

    public void InitializeHeld(IEnumerable<int> heldKeys)
    {
        ArgumentNullException.ThrowIfNull(heldKeys);
        _down.Clear();
        foreach (int key in heldKeys)
            _down.Add(key);
        _ownedKeyUps.IntersectWith(_down);
    }

    public void Cancel()
    {
        if (!_recording)
            return;

        Stop(cancelled: true);
    }

    public bool Key(int vk, bool down, bool injected = false)
    {
        if (injected)
            return false;
        return down ? KeyDown(vk) : KeyUp(vk);
    }

    public int[]? TakeCompleted()
    {
        int[]? result = _completed;
        _completed = null;
        return result;
    }

    public bool TakeCancelled()
    {
        bool result = _cancelled;
        _cancelled = false;
        return result;
    }

    private bool KeyDown(int vk)
    {
        bool firstDown = _down.Add(vk);
        if (_recording && vk == VkEscape)
        {
            if (firstDown)
                _ownedKeyUps.Add(vk);
            Stop(cancelled: true);
            return true;
        }
        if (_ownedKeyUps.Contains(vk))
            return true;
        if (!_recording)
            return false;
        if (_waitingForNeutral)
            return false;

        if (firstDown)
        {
            _captureStarted = true;
            _captured.Add(vk);
            _ownedKeyUps.Add(vk);
        }
        return firstDown || _ownedKeyUps.Contains(vk);
    }

    private bool KeyUp(int vk)
    {
        bool wasDown = _down.Contains(vk);
        bool owned = _ownedKeyUps.Remove(vk);

        if (_recording && _waitingForNeutral)
        {
            _down.Remove(vk);
            if (_down.Count == 0)
                _waitingForNeutral = false;
            return owned;
        }

        if (_recording && _captureStarted && !_capturedAtFirstRelease && wasDown && _captured.Contains(vk))
        {
            _capturedAtFirstRelease = true;
            _pendingCompleted = _captured.Where(_down.Contains).ToArray();
        }

        _down.Remove(vk);
        if (_recording && _capturedAtFirstRelease && _down.Count == 0)
        {
            _completed = _pendingCompleted;
            _pendingCompleted = null;
            Stop(cancelled: false);
        }

        return owned;
    }

    private void Stop(bool cancelled)
    {
        _recording = false;
        _waitingForNeutral = false;
        _captureStarted = false;
        _capturedAtFirstRelease = false;
        _captured.Clear();
        _pendingCompleted = null;
        if (cancelled)
        {
            _completed = null;
            _cancelled = true;
        }
    }
}
