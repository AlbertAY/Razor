// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Razor.Language.Legacy;

namespace Microsoft.AspNetCore.Razor.Language.Extensions
{
    public static class SectionDirective
    {
        public static readonly DirectiveDescriptor Directive = DirectiveDescriptor.CreateDirective(
            SyntaxConstants.CSharp.SectionKeyword,
            DirectiveKind.RazorBlock,
            builder =>
            {
                builder.AddMemberToken(Resources.SectionDirective_NameToken_Name, Resources.SectionDirective_NameToken_Description);
                builder.Description = Resources.SectionDirective_Description;
            });

        public static void Register(IRazorEngineBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            // ---------------------------------------------------------------------------------------------
            // When updating these registrations also update the RazorProjectEngineBuilder overload as well.
            // ---------------------------------------------------------------------------------------------

            builder.AddDirective(Directive);
            builder.Features.Add(new SectionDirectivePass());
            builder.AddTargetExtension(new SectionTargetExtension());
        }

        public static void Register(RazorProjectEngineBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            // ----------------------------------------------------------------------------------------------------------
            // When updating the RazorEngine specific registrations also update the IRazorEngineBuilder overload as well.
            // ----------------------------------------------------------------------------------------------------------

            builder.AddDirective(Directive);
            builder.Features.Add(new SectionDirectivePass());
            builder.AddTargetExtension(new SectionTargetExtension());
        }
    }
}
