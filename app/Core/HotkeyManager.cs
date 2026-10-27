using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace PrecisionCurve.Core
{
    /// <summary>
    /// Global hotkeys via RegisterHotKey on a message-only window.
    ///
    /// RegisterHotKey is deliberately used instead of a low-level keyboard hook:
    /// a hook would see every keystroke on the system (a keylogger by another
    /// name) and needs the same privileges as our driver.
    /// </summary>
    public sealed class HotkeyManager : IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const int HwndMessage = -3;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;
        private const uint ModNoRepeat = 0x4000;

        private readonly Dictionary<int, Action> _actions = new Dictionary<int, Action>();
        private HwndSource _source;
        private int _nextId = 1;
        private bool _disposed;

        public bool IsAvailable => _source != null;

        private void EnsureWindow()
        {
            if (_source != null) return;

            var parameters = new HwndSourceParameters("PrecisionCurveHotkeys")
            {
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                WindowStyle = 0,
                ParentWindow = new IntPtr(HwndMessage)
            };

            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        /// <summary>
        /// Registers a gesture such as "Ctrl+Alt+1" or "Shift+F2".
        /// Returns false with a reason when the combo is already taken.
        /// </summary>
        public bool TryRegister(string gesture, Action action, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(gesture) || action == null)
            {
                error = "Empty gesture";
                return false;
            }

            if (!TryParseGesture(gesture, out uint modifiers, out uint vk, out error))
            {
                return false;
            }

            EnsureWindow();
            int id = _nextId++;

            if (!RegisterHotKey(_source.Handle, id, modifiers | ModNoRepeat, vk))
            {
                error = "Already in use by another application";
                return false;
            }

            _actions[id] = action;
            return true;
        }

        public void UnregisterAll()
        {
            if (_source == null) return;

            foreach (int id in _actions.Keys)
            {
                UnregisterHotKey(_source.Handle, id);
            }
            _actions.Clear();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception)
                {
                    // A hotkey handler must never take the UI thread down.
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        public static bool TryParseGesture(string gesture, out uint modifiers, out uint vk, out string error)
        {
            modifiers = 0;
            vk = 0;
            error = null;

            string[] parts = gesture.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = "Empty gesture";
                return false;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();

                if (i < parts.Length - 1)
                {
                    switch (token.ToUpperInvariant())
                    {
                        case "CTRL":
                        case "CONTROL":
                            modifiers |= ModControl;
                            break;
                        case "ALT":
                            modifiers |= ModAlt;
                            break;
                        case "SHIFT":
                            modifiers |= ModShift;
                            break;
                        case "WIN":
                        case "WINDOWS":
                            modifiers |= ModWin;
                            break;
                        default:
                            error = $"Unknown modifier '{token}'";
                            return false;
                    }
                }
                else
                {
                    string keyName = token.ToUpperInvariant();
                    Key key;

                    if (keyName.Length == 1 && char.IsDigit(keyName[0]))
                    {
                        key = (Key)((int)Key.D0 + (keyName[0] - '0'));
                    }
                    else if (!Enum.TryParse(keyName, out key))
                    {
                        error = $"Unknown key '{token}'";
                        return false;
                    }

                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                }
            }

            if (vk == 0)
            {
                error = "Gesture has no key";
                return false;
            }
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            UnregisterAll();

            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source.Dispose();
                _source = null;
            }
        }
    }
}
