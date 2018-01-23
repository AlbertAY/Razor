// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Razor.Language.Legacy;

namespace Microsoft.AspNetCore.Razor.Language.Extensions
{
    public static class FunctionsDirective
    {
        public static readonly DirectiveDescriptor Directive = DirectiveDescriptor.CreateDirective(
            SyntaxConstants.CSharp.FunctionsKeyword,
            DirectiveKind.CodeBlock,
            builder =>
            {
                builder.Description = Resources.FunctionsDirective_Description;
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
            builder.Features.Add(new FunctionsDirectivePass());
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
            builder.Features.Add(new FunctionsDirectivePass());
        }
    }
}
