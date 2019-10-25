// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace System.Windows.Forms.Automation
{
    [Flags]
    internal enum SendMouseInputFlags
    {
        // Specifies that the pointer moved.
        Move = 0x0001,

        // Specifies that the left button was pressed.
        LeftDown = 0x0002,

        // Specifies that the left button was released.
        LeftUp = 0x0004,

        // Specifies that the right button was pressed.
        RightDown = 0x0008,

        // Specifies that the right button was released.
        RightUp = 0x0010,

        // Specifies that the middle button was pressed.
        MiddleDown = 0x0020,

        // Specifies that the middle button was released.
        MiddleUp = 0x0040,

        // Specifies that the x button was pressed.
        XDown = 0x0080,

        // Specifies that the x button was released. 
        XUp = 0x0100,

        // Specifies that the wheel was moved
        Wheel = 0x0800,

        // Specifies that x, y are absolute, not relative
        Absolute = 0x8000,
    };

    internal abstract class UiaTextProvider : UnsafeNativeMethods.ITextProvider
    {
        public abstract UnsafeNativeMethods.ITextRangeProvider DocumentRangeInternal { get; }

        public abstract int GetFirstVisibleLine();

        public abstract int GetLineCount();

        public abstract int GetLineIndex(int line);

        public abstract int GetLineFromCharIndex(int charIndex);

        public abstract int GetLinesPerPage();

        public abstract NativeMethods.LOGFONT GetLogfont();

        public abstract Point GetPositionFromChar(int charIndex);

        public abstract Point GetPositionFromCharUR(int startCharIndex, string text);

        public abstract Rectangle GetRectangle();

        public abstract string GetText();

        public abstract int GetTextLength();

        public abstract void GetVisibleRangePoints(out int visibleStart, out int visibleEnd);

        public abstract bool IsMultiline { get; }

        public abstract bool IsReadOnly { get; }

        public abstract bool IsScrollable { get; }

        public abstract bool LineScroll(int charactersHorizontal, int linesVertical);

        public abstract int LinesPerPage { get; }

        public abstract Control OwningControl { get; }

        public abstract void SetSelection(int start, int end);

        public abstract UnsafeNativeMethods.SupportedTextSelection SupportedTextSelectionInternal { get; }

        public UnsafeNativeMethods.ITextRangeProvider DocumentRange => DocumentRangeInternal;

        public UnsafeNativeMethods.ITextRangeProvider[] GetSelection() => GetSelectionInternal();

        public UnsafeNativeMethods.ITextRangeProvider[] GetVisibleRanges() => GetVisibleRangesInternal();

        public UnsafeNativeMethods.ITextRangeProvider RangeFromChild(UnsafeNativeMethods.IRawElementProviderSimple childElement) =>
            RangeFromChildInternal(childElement);

        public UnsafeNativeMethods.ITextRangeProvider RangeFromPoint(Point screenLocation) => RangeFromPointInternal(screenLocation);

        public UnsafeNativeMethods.SupportedTextSelection SupportedTextSelection => SupportedTextSelectionInternal;

        public abstract UnsafeNativeMethods.ITextRangeProvider[] GetSelectionInternal();

        public abstract UnsafeNativeMethods.ITextRangeProvider[] GetVisibleRangesInternal();

        public abstract UnsafeNativeMethods.ITextRangeProvider RangeFromChildInternal(UnsafeNativeMethods.IRawElementProviderSimple childElement);

        public abstract UnsafeNativeMethods.ITextRangeProvider RangeFromPointInternal(Point screenLocation);

        public static IntPtr GetFocusedWindow()
        {
            NativeMethods.GUITHREADINFO gui;

            return GetGuiThreadInfo(0, out gui) ? gui.hwndFocus : IntPtr.Zero;
        }

        public static bool GetMessage(ref NativeMethods.MSG msg, IntPtr hwnd, int msgFilterMin, int msgFilterMax)
        {
            int result = UnsafeNativeMethods.GetMessage(ref msg, hwnd, msgFilterMin, msgFilterMax);
            int lastWin32Error = Marshal.GetLastWin32Error();

            bool success = (result != 0 && result != -1);
            if (!success)
            {
                ThrowWin32ExceptionsIfError(lastWin32Error);
            }

            return success;
        }

        public static string GetText(IntPtr hwnd, int length)
        {
            if (length == 0)
            {
                return "";
            }

            // Length passes to SendMessage includes terminating NUL.
            Text.StringBuilder str = new Text.StringBuilder(length + 1);
            UnsafeNativeMethods.SendMessage(new HandleRef(null, hwnd), Interop.WindowMessages.WM_GETTEXT, str.Capacity, str);
            return str.ToString();
        }

        public static int GetWindowExStyle(IntPtr hwnd)
        {
            IntPtr exstyle = UnsafeNativeMethods.GetWindowLong(new HandleRef(null, hwnd), NativeMethods.GWL_EXSTYLE);

            if (exstyle == IntPtr.Zero)
            {
                int lastWin32Error = Marshal.GetLastWin32Error();
                Debug.Fail("Win32 error: " + lastWin32Error);
            }

            return (int)exstyle;
        }

        public static short GlobalAddAtom(string atomName)
        {
            short atom = UnsafeNativeMethods.GlobalAddAtom(atomName);
            int lastWin32Error = Marshal.GetLastWin32Error();
            if (atom == 0)
            {
                ThrowWin32ExceptionsIfError(lastWin32Error);
            }

            return atom;
        }

        public static short GlobalDeleteAtom(short atom)
        {
            short result = UnsafeNativeMethods.GlobalDeleteAtom(atom);
            ThrowWin32ExceptionsIfError(Marshal.GetLastWin32Error());
            return result;
        }

        public bool IsReadingRTL(IntPtr hwnd)
        {
            int style = GetWindowExStyle(hwnd);
            return (style & NativeMethods.WS_EX_RTLREADING) == NativeMethods.WS_EX_RTLREADING;
        }

        public static bool GetGuiThreadInfo(uint idThread, out NativeMethods.GUITHREADINFO gui)
        {
            gui = new NativeMethods.GUITHREADINFO();
            gui.cbSize = Marshal.SizeOf(gui.GetType());

            bool result = UnsafeNativeMethods.GetGUIThreadInfo(idThread, ref gui);
            int lastWin32Error = Marshal.GetLastWin32Error();

            if (!result)
            {
                // If the focused thread is on another [secure] desktop, GetGUIThreadInfo
                // will fail with ERROR_ACCESS_DENIED - don't throw an exception for that case,
                // instead treat as a failure. Callers will treat this as though no window has
                // focus.
                if (lastWin32Error == 5 /*ERROR_ACCESS_DENIED*/)
                {
                    return false;
                }

                Debug.Fail("Win32 error: " + lastWin32Error);
            }

            return result;
        }

        public double[] RectArrayToDoubleArray(Drawing.Rectangle[] rectArray)
        {
            if (rectArray == null)
            {
                return null;
            }

            double[] doubles = new double[rectArray.Length * 4];
            int scan = 0;
            for (int i = 0; i < rectArray.Length; i++)
            {
                doubles[scan++] = rectArray[i].X;
                doubles[scan++] = rectArray[i].Y;
                doubles[scan++] = rectArray[i].Width;
                doubles[scan++] = rectArray[i].Height;
            }

            return doubles;
        }

        internal static bool RegisterHotKey(IntPtr hwnd, short atom, int modifiers, int vk)
        {
            bool result = UnsafeNativeMethods.RegisterHotKey(hwnd, atom, modifiers, vk);
            int lastWin32Error = Marshal.GetLastWin32Error();

            if (!result)
            {
                ThrowWin32ExceptionsIfError(lastWin32Error);
            }

            return result;
        }

        public static int ReleaseDC(IntPtr hwnd, IntPtr hdc)
        {
            // If ReleaseDC fails we will not do anything with that information so just ignore the
            // PRESHARP warnings.
            return Interop.User32.ReleaseDC(hwnd, hdc);
        }

        internal static string RealGetWindowClass(IntPtr hwnd)
        {
            Text.StringBuilder className = new Text.StringBuilder(Interop.Kernel32.MAX_PATH + 1);

            uint result = UnsafeNativeMethods.RealGetWindowClass(hwnd, className, Interop.Kernel32.MAX_PATH);
            int lastWin32Error = Marshal.GetLastWin32Error();

            if (result == 0)
            {
                ThrowWin32ExceptionsIfError(lastWin32Error);
                return "";
            }

            return className.ToString();
        }

        internal static void ThrowWin32ExceptionsIfError(int errorCode)
        {
            switch (errorCode)
            {
                case 0:     //    0 ERROR_SUCCESS                   The operation completed successfully.
                            // The error code indicates that there is no error, so do not throw an exception.
                    break;

                case 6:     //    6 ERROR_INVALID_HANDLE            The handle is invalid.
                case 1400:  // 1400 ERROR_INVALID_WINDOW_HANDLE     Invalid window handle.
                case 1401:  // 1401 ERROR_INVALID_MENU_HANDLE       Invalid menu handle.
                case 1402:  // 1402 ERROR_INVALID_CURSOR_HANDLE     Invalid cursor handle.
                case 1403:  // 1403 ERROR_INVALID_ACCEL_HANDLE      Invalid accelerator table handle.
                case 1404:  // 1404 ERROR_INVALID_HOOK_HANDLE       Invalid hook handle.
                case 1405:  // 1405 ERROR_INVALID_DWP_HANDLE        Invalid handle to a multiple-window position structure.
                case 1406:  // 1406 ERROR_TLW_WITH_WSCHILD          Cannot create a top-level child window.
                case 1407:  // 1407 ERROR_CANNOT_FIND_WND_CLASS     Cannot find window class.
                case 1408:  // 1408 ERROR_WINDOW_OF_OTHER_THREAD    Invalid window; it belongs to other thread.
                    throw new ExternalException();

                // We're getting this in AMD64 when calling RealGetWindowClass; adding this code
                // to allow the DRTs to pass while we continue investigation.
                case 87:    //   87 ERROR_INVALID_PARAMETER
                    throw new ExternalException();

                case 8:     //    8 ERROR_NOT_ENOUGH_MEMORY         Not enough storage is available to process this command.
                case 14:    //   14 ERROR_OUTOFMEMORY               Not enough storage is available to complete this operation.
                    throw new OutOfMemoryException();

                case 998:   //  998 ERROR_NOACCESS                  Invalid access to memory location.
                case 5:     //    5 ERROR_ACCESS_DENIED
                    throw new InvalidOperationException();

                case 121:   //  121 ERROR_SEM_TIMEOUT               The semaphore timeout period has expired.
                case 258:   //  258 WAIT_TIMEOUT                    The wait operation timed out.
                case 1053:  // 1053 ERROR_SERVICE_REQUEST_TIMEOUT   The service did not respond to the start or control request in a timely fashion.
                case 1460:  // 1460 ERROR_TIMEOUT                   This operation returned because the timeout period expired.
                    throw new TimeoutException();

                default:
                    // Not sure how to map the reset of the error codes so throw generic Win32Exception.
                    throw new System.ComponentModel.Win32Exception(errorCode);
            }
        }

        internal static bool UnregisterHotKey(IntPtr hwnd, short atom)
        {
            bool result = UnsafeNativeMethods.UnregisterHotKey(hwnd, atom);
            int lastWin32Error = Marshal.GetLastWin32Error();

            if (!result)
            {
                ThrowWin32ExceptionsIfError(lastWin32Error);
            }

            return result;
        }

        public int SendInput(int inputs, ref NativeMethods.INPUT ki, int size)
        {
            int eventCount = UnsafeNativeMethods.SendInput(inputs, ref ki, size);
            int lastWin32Error = Marshal.GetLastWin32Error();

            if (eventCount <= 0)
            {
                ThrowWin32ExceptionsIfError(lastWin32Error);
            }

            return eventCount;
        }

        public static void SendMouseInput(double x, double y, int data, SendMouseInputFlags flags)
        {
            // Injects pointer input into the system x, y are in pixels.
            // If Absolute flag used, are relative to desktop origin.

            int intflags = (int)flags;

            if ((intflags & (int)SendMouseInputFlags.Absolute) != 0)
            {
                int vscreenWidth = Interop.User32.GetSystemMetrics(Interop.User32.SystemMetric.SM_CXVIRTUALSCREEN);
                int vscreenHeight = Interop.User32.GetSystemMetrics(Interop.User32.SystemMetric.SM_CYVIRTUALSCREEN);
                int vscreenLeft = Interop.User32.GetSystemMetrics(Interop.User32.SystemMetric.SM_XVIRTUALSCREEN);
                int vscreenTop = Interop.User32.GetSystemMetrics(Interop.User32.SystemMetric.SM_YVIRTUALSCREEN);

                // Absolute input requires that input is in 'normalized' coords - with the entire
                // desktop being (0,0)...(65535,65536). Need to convert input x,y coords to this
                // first.
                //
                // In this normalized world, any pixel on the screen corresponds to a block of values
                // of normalized coords - eg. on a 1024x768 screen,
                // y pixel 0 corresponds to range 0 to 85.333,
                // y pixel 1 corresponds to range 85.333 to 170.666,
                // y pixel 2 correpsonds to range 170.666 to 256 - and so on.
                // Doing basic scaling math - (x-top)*65536/Width - gets us the start of the range.
                // However, because int math is used, this can end up being rounded into the wrong
                // pixel. For example, if we wanted pixel 1, we'd get 85.333, but that comes out as
                // 85 as an int, which falls into pixel 0's range - and that's where the pointer goes.
                // To avoid this, we add on half-a-"screen pixel"'s worth of normalized coords - to
                // push us into the middle of any given pixel's range - that's the 65536/(Width*2)
                // part of the formula. So now pixel 1 maps to 85+42 = 127 - which is comfortably
                // in the middle of that pixel's block.
                // The key ting here is that unlike points in coordinate geometry, pixels take up
                // space, so are often better treated like rectangles - and if you want to target
                // a particular pixel, target its rectangle's midpoint, not its edge.
                x = ((x - vscreenLeft) * 65536) / vscreenWidth + 65536 / (vscreenWidth * 2);
                y = ((y - vscreenTop) * 65536) / vscreenHeight + 65536 / (vscreenHeight * 2);

                intflags |= Interop.WinUser.MOUSEEVENTF_VIRTUALDESK;
            }

            NativeMethods.INPUT mi = new NativeMethods.INPUT();
            mi.type = NativeMethods.INPUT_MOUSE;
            mi.inputUnion.mi.dx = (int)x;
            mi.inputUnion.mi.dy = (int)y;
            mi.inputUnion.mi.mouseData = data;
            mi.inputUnion.mi.dwFlags = intflags;
            mi.inputUnion.mi.time = 0;
            mi.inputUnion.mi.dwExtraInfo = new IntPtr(0);
            if (UnsafeNativeMethods.SendInput(1, ref mi, Marshal.SizeOf(mi)) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public static void SendKeyboardInput(Keys key, bool press)
        {
            // Injects keyboard input into the system.

            NativeMethods.INPUT ki = new NativeMethods.INPUT();
            ki.type = NativeMethods.INPUT_KEYBOARD;
            ki.inputUnion.ki.wVk = (short)key;
            ki.inputUnion.ki.wScan = (short)Interop.WinUser.MapVirtualKey(ki.inputUnion.ki.wVk, 0);
            int dwFlags = 0;
            if (ki.inputUnion.ki.wScan > 0)
            {
                dwFlags |= Interop.WinUser.KEYEVENTF_SCANCODE;
            }

            if (!press)
            {
                dwFlags |= NativeMethods.KEYEVENTF_KEYUP;
            }

            ki.inputUnion.ki.dwFlags = dwFlags;
            if (IsExtendedKey(ki.inputUnion.ki.wVk))
            {
                ki.inputUnion.ki.dwFlags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            }

            ki.inputUnion.ki.time = 0;
            ki.inputUnion.ki.dwExtraInfo = new IntPtr(0);
            if (UnsafeNativeMethods.SendInput(1, ref ki, Marshal.SizeOf(ki)) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public void SendKeyboardInputVK(short vk, bool press)
        {
            NativeMethods.INPUT ki = new NativeMethods.INPUT();

            ki.type = NativeMethods.INPUT_KEYBOARD;
            ki.inputUnion.ki.wVk = vk;
            ki.inputUnion.ki.wScan = 0;
            ki.inputUnion.ki.dwFlags = press ? 0 : NativeMethods.KEYEVENTF_KEYUP;
            if (IsExtendedKey(vk))
            {
                ki.inputUnion.ki.dwFlags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            }

            ki.inputUnion.ki.time = 0;
            ki.inputUnion.ki.dwExtraInfo = new IntPtr(0);

            SendInput(1, ref ki, Marshal.SizeOf(ki));
        }

        #region Private Methods

        private static bool IsExtendedKey(short vk)
        {
            // From the SDK:
            // The extended-key flag indicates whether the keystroke message originated from one of
            // the additional keys on the enhanced keyboard. The extended keys consist of the ALT and
            // CTRL keys on the right-hand side of the keyboard; the INS, DEL, HOME, END, PAGE UP,
            // PAGE DOWN, and arrow keys in the clusters to the left of the numeric keypad; the NUM LOCK
            // key; the BREAK (CTRL+PAUSE) key; the PRINT SCRN key; and the divide (/) and ENTER keys in
            // the numeric keypad. The extended-key flag is set if the key is an extended key.
            //
            // - docs appear to be incorrect. Use of Spy++ indicates that break is not an extended key.
            // Also, menu key and windows keys also appear to be extended.
            return vk == unchecked((short)UnsafeNativeMethods.VK_RMENU) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_RCONTROL) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_NUMLOCK) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_INSERT) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_DELETE) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_HOME) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_END) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_PRIOR) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_NEXT) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_UP) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_DOWN) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_LEFT) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_RIGHT) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_APPS) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_RWIN) ||
                   vk == unchecked((short)UnsafeNativeMethods.VK_LWIN);
            // Note that there are no distinct values for the following keys:
            // numpad divide
            // numpad enter
        }

        #endregion Private Methods
    }
}
