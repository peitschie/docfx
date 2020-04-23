// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Markdig;
using Microsoft.DocAsCode.MarkdigEngine.Extensions;
using Validations.DocFx.Adapter;

namespace Microsoft.Docs.Build
{
    internal static class ContentValidationExtension
    {
        public static MarkdownPipelineBuilder UseContentValidation(
            this MarkdownPipelineBuilder builder, OnlineServiceMarkdownValidatorProvider? validatorProvider)
        {
            if (validatorProvider == null)
            {
                return builder;
            }

            var validators = validatorProvider.GetValidators();

            return builder.Use(document =>
            {
                if (((Document)InclusionContext.File).FilePath.Format == FileFormat.Markdown)
                {
                    foreach (var validator in validators)
                    {
                        validator.Validate(document);
                    }
                }
            });
        }
    }
}