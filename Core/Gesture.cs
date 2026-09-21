using System;
using System.Collections.Generic;

namespace Mix.Core;

public enum GestureAction { Show, Hide, Left, Right, Up, Down, Mute }

public record struct KeyDecision(bool Suppress, GestureAction? Action);

public class Gesture
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkEscape = 0x1B;

    private readonly HashSet<int> _down = new();
    private readonly HashSet<int> _ownedKeyUps = new();
    private KeyboardBindings _bindings;
    private bool _armed;
    private bool _active;
    private int _wheelRemainder;

    public Gesture(KeyboardBindings? bindings = null)
    {
        _bindings = bindings ?? KeyboardBindings.Default;
        if (_bindings.Validate() is { } error)
            throw new ArgumentException(error, nameof(bindings));
    }

    public bool Active => _active;
    public KeyboardBindings Bindings => _bindings;

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

    public void SetBindings(KeyboardBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (bindings.Validate() is { } error)
            throw new ArgumentException(error, nameof(bindings));

        _bindings = bindings;
        _active = false;
        _wheelRemainder = 0;
        _armed = _down.Count == 0 && _ownedKeyUps.Count == 0;
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
                return new(true, vk == _bindings.Mute && !firstDown ? null : action);
            }

            if (_ownedKeyUps.Contains(vk))
                return new(true, null);

            if (!_bindings.Opening.Contains(vk))
                return Hide(suppress: false);

            return default;
        }

        if (_ownedKeyUps.Contains(vk))
            return new(true, null);

        if (_armed && firstDown && _down.Count == _bindings.Opening.Count &&
            _bindings.Opening.All(_down.Contains))
        {
            _active = true;
            _wheelRemainder = 0;
            _ownedKeyUps.Add(vk);
            return new(true, GestureAction.Show);
        }

        return default;
    }

    private KeyDecision KeyUp(int vk)
    {
        bool openingRelease = _active && _bindings.Opening.Contains(vk);
        _down.Remove(vk);
        bool owned = _ownedKeyUps.Remove(vk);

        if (openingRelease)
            return Hide(suppress: owned);

        if (owned)
        {
            if (_down.Count == 0)
                _armed = true;
            return new(true, null);
        }

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

    private GestureAction? ActionFor(int vk) => vk switch
    {
        VkLeft => GestureAction.Left,
        VkRight => GestureAction.Right,
        VkUp => GestureAction.Up,
        VkDown => GestureAction.Down,
        _ when vk == _bindings.Mute => GestureAction.Mute,
        _ => null
    };
}
