// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace System.Windows.Forms.Automation
{
    [ComVisible(true)]
    [Guid("4E33C74B-7848-4f1e-B819-A0D866C2EA1F")]
    public enum CapStyle
    {
        Other = -1,
        None = 0,
        SmallCap = 1,
        AllCap = 2,
        AllPetiteCaps = 3,
        PetiteCaps = 4,
        Unicase = 5,
        Titling = 6
    }
}
