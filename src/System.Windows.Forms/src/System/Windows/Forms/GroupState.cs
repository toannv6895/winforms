// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Windows.Forms
{
    /// <summary>
    ///  Values to be passed through to <see cref="ListViewGroup.CollapsedState"/> to specify ListViewGroup state.
    /// </summary>
    public enum GroupState
    {
        /// <summary>
        ///  ListViewGroup will appear expanded and will not be collapsible.
        /// </summary>
        Normal,

        /// <summary>
        ///  ListViewGroup will appear expanded and will be collapsible.
        /// </summary>
        Expanded,

        /// <summary>
        ///  ListViewGroup will appear collapsed and will be expandable.
        /// </summary>
        Collapsed
    }
}
