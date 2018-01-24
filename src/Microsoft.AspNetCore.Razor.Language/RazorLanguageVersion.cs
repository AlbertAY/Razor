// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Razor.Language
{
    public sealed class RazorLanguageVersion
    {
        public static readonly RazorLanguageVersion Version_1_0 = new RazorLanguageVersion(1, 0);

        public static readonly RazorLanguageVersion Version_1_1 = new RazorLanguageVersion(1, 1);

        public static readonly RazorLanguageVersion Version_2_0 = new RazorLanguageVersion(2, 0);

        public static readonly RazorLanguageVersion Version_2_1 = new RazorLanguageVersion(2, 1);

        public static readonly RazorLanguageVersion Latest = Version_2_1;

        // Don't want anyone else constructing language versions.
        private RazorLanguageVersion(int major, int minor)
        {
            Major = major;
            Minor = minor;
        }

        public int Major { get; }

        public int Minor { get; }

        public override string ToString() => $"Razor Language '{Major}.{Minor}'";
    }
}
