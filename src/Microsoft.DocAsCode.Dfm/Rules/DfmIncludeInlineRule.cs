// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.DocAsCode.Dfm
{
    using System.Text.RegularExpressions;

    using Microsoft.DocAsCode.MarkdownLite;

    public class DfmIncludeInlineRule : IMarkdownRule
    {
        public virtual string Name => "DfmIncludeInline";
        private static readonly Regex _inlineIncludeRegex = new Regex(DocfxFlavoredIncHelper.InlineIncRegexString, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public virtual Regex Include => _inlineIncludeRegex;

        public IMarkdownToken TryMatch(IMarkdownParser parser, ref string source)
        {
            var match = Include.Match(source);
            if (match.Length == 0)
            {
                return null;
            }
            source = source.Substring(match.Length);

            // [!include[title](path "optionalTitle")]
            // 1. Get include file path 
            var path = match.Groups[2].Value;

            // 2. Get title
            var value = match.Groups[1].Value;
            var title = match.Groups[4].Value;

            // 3. Apply inline rules to the included content
            return new DfmIncludeInlineToken(this, parser.Context, path, value, title, match.Groups[0].Value, match.Value);
        }
    }
}
