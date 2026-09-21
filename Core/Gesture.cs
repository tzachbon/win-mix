using System;
using System.Collections.Generic;

namespace Mix.Core;

public enum GestureAction { Show, Hide, Left, Right, Up, Down, Mute }

public record struct KeyDecision(bool Suppress, GestureAction? Action);

public class Gesture
{
    private const int VkLControl = 0xA2;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkM = 0x4D;
    private const int VkEscape = 0x1B;

    private readonly HashSet<int> _down = new();
    private readonly HashSet<int> _ownedKeyUps = new();
    private bool _armed;
    private bool _active;
    private int _wheelRemainder;

    public bool Active => _active;

    public KeyDecision Key(int vk, bool down, bool injected = false)
    {
        if (injected)
            return default;

        return down ? KeyDown(vk) : KeyUp(vk);
    }

    public int Wheel(int delta)
    {
        if (!_active)
        {
            _wheelRemainder = 0;
            return 0;
        }

        long total = (long)_wheelRemainder + delta;
        int steps = (int)(total / 120);
        _wheelRemainder = (int)(total % 120);
        return steps;
    }

    public void Reset()
    {
        _active = false;
        _armed = false;
        _wheelRemainder = 0;
    }

    public void InitializeHeld(IEnumerable<int> heldKeys)
    {
        ArgumentNullException.ThrowIfNull(heldKeys);

        _active = false;
        _wheelRemainder = 0;
        _down.Clear();
        foreach (int vk in heldKeys)
            if (vk >= 8 && vk is not (VkShift or VkControl or VkMenu))
                _down.Add(vk);

        _ownedKeyUps.IntersectWith(_down);
        _armed = _down.Count == 0;
    }

    private KeyDecision KeyDown(int vk)
    {
        bool firstDown = _down.Add(vk);

        if (_active)
        {
            if (vk == VkEscape)
            {
                _ownedKeyUps.Add(vk);
                return Hide(suppress: true);
            }

            GestureAction? action = ActionFor(vk);
            if (action.HasValue)
            {
                _ownedKeyUps.Add(vk);
                return new(true, vk == VkM && !firstDown ? null : action);
            }

            if (vk is not (VkLControl or VkLMenu))
                return Hide(suppress: false);

            return default;
        }

        if (_ownedKeyUps.Contains(vk))
            return new(true, null);

        if (_armed && firstDown && (vk is VkLControl or VkLMenu) && _down.Count == 2 &&
            _down.Contains(VkLControl) && _down.Contains(VkLMenu))
        {
            _active = true;
            _wheelRemainder = 0;
            return new(false, GestureAction.Show);
        }

        return default;
    }

    private KeyDecision KeyUp(int vk)
    {
        _down.Remove(vk);

        if (_ownedKeyUps.Remove(vk))
        {
            if (_down.Count == 0)
                _armed = true;
            return new(true, null);
        }

        if (_active && (vk is VkLControl or VkLMenu))
            return Hide(suppress: false);

        if (_down.Count == 0)
            _armed = true;

        return default;
    }

    private KeyDecision Hide(bool suppress)
    {
        _active = false;
        _armed = _down.Count == 0;
        _wheelRemainder = 0;
        return new(suppress, GestureAction.Hide);
    }

    private static GestureAction? ActionFor(int vk) => vk switch
    {
        VkLeft => GestureAction.Left,
        VkRight => GestureAction.Right,
        VkUp => GestureAction.Up,
        VkDown => GestureAction.Down,
        VkM => GestureAction.Mute,
        _ => null
    };
}
