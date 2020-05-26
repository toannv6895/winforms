// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Windows.Forms
{
    /// <summary>
    ///  Provides index of the group for ListViewGroup events.
    /// </summary>
    public class ListViewGroupEventArgs : EventArgs
    {
        /// <summary>
        ///  Constructor for ListViewGroupEventArgs.
        /// </summary>
        public ListViewGroupEventArgs(int groupIndex)
        {
            GroupIndex = groupIndex;
        }

        /// <summary>
        ///  Returns the index of the ListViewGroup associated with the event.
        /// </summary>
        public int GroupIndex { get; }
    }
}
