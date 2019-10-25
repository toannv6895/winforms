// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;

internal static partial class Interop
{
    public static partial class WinUser
    {
        public const int MOUSEEVENTF_VIRTUALDESK = 0x4000;

        internal const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        internal const int KEYEVENTF_KEYUP = 0x0002;
        internal const int KEYEVENTF_SCANCODE = 0x0008;

        [DllImport(ExternDll.User32, CharSet = CharSet.Unicode)]
        public static extern int MapVirtualKey(int nCode, int nMapType);
    }
}
