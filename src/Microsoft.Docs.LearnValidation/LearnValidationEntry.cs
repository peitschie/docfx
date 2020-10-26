// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Docs.LearnValidation.Models;
using Microsoft.TripleCrown.Hierarchy.DataContract.Hierarchy;
using Newtonsoft.Json;

namespace Microsoft.Docs.LearnValidation
{
    public static class LearnValidationEntry
    {
        private const string PluginName = "LearnValidationPlugin";

        public static void Run(
            string repoUrl,
            string repoBranch,
            string docsetName,
            string docsetPath,
            string docsetOutputPath,
            string publishFilePath,
            string dependencyFilePath,
            string manifestFilePath,
            string environment,
            bool isLocalizationBuild,
            bool noDrySync,
            Action<LearnLogItem> writeLog,
            ILearnServiceAccessor learnServiceAccessor,
            string fallbackDocsetPath = null)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var config = new LearnValidationConfig(
                repoUrl: repoUrl,
                repoBranch: repoBranch,
                docsetName: docsetName,
                docsetPath: docsetPath,
                docsetOutputPath: docsetOutputPath,
                publishFilePath: publishFilePath,
                dependencyFilePath: dependencyFilePath,
                manifestFilePath: manifestFilePath,
                environment: environment,
                fallbackDocsetPath: fallbackDocsetPath,
                isLocalizationBuild: isLocalizationBuild,
                noDrySync: noDrySync);
            var logger = new LearnValidationLogger(writeLog);

            var configStr = JsonConvert.SerializeObject(
                new { repoUrl, repoBranch, docsetName, docsetPath, publishFilePath, dependencyFilePath, manifestFilePath, isLocalizationBuild, environment, fallbackDocsetPath },
                Formatting.Indented);

            Console.WriteLine($"[{PluginName}] config:\n{configStr}");
            ValidateHierarchy(config, logger, learnServiceAccessor).GetAwaiter().GetResult();
        }

        private static async Task<bool> ValidateHierarchy(LearnValidationConfig config, LearnValidationLogger logger, ILearnServiceAccessor learnServiceAccessor)
        {
            var sw = Stopwatch.StartNew();
            Console.WriteLine($"[{PluginName}] start to do local validation.");

            var learnValidationHelper = new LearnValidationHelper(config.RepoBranch, learnServiceAccessor);
            var validator = new Validator(learnValidationHelper, manifestFilePath: config.ManifestFilePath, logger);
            var (isValid, hierarchyItems) = validator.Validate();

            Console.WriteLine($"[{PluginName}] local validation done in {sw.ElapsedMilliseconds / 1000}s");

            if (!config.IsLocalizationBuild)
            {
                return await ValidateHierarchyInDefaultLocale(isValid, hierarchyItems, config, logger, learnServiceAccessor);
            }
            else
            {
                return ValidateHierarchyInOtherLocales(isValid, hierarchyItems, config, learnValidationHelper, logger);
            }
        }

        private static async Task<bool> ValidateHierarchyInDefaultLocale(
            bool isValid,
            List<IValidateModel> hierarchyItems,
            LearnValidationConfig config,
            LearnValidationLogger logger,
            ILearnServiceAccessor learnServiceAccessor)
        {
            if (!isValid)
            {
                return false;
            }

            var hierarchy = HierarchyGenerator.GenerateHierarchy(hierarchyItems, config.DocsetOutputPath);
            var repoUrl = Utility.TransformGitUrl(config.RepoUrl);

            if (config.NoDrySync)
            {
                Console.WriteLine($"Skipping dry-sync");
                return true;
            }

            var result = await TryDrySync(
                config.RepoBranch,
                Constants.DefaultLocale,
                config.DocsetName,
                repoUrl,
                hierarchy,
                learnServiceAccessor);

            if (!result.IsValid)
            {
                logger.Log(LearnErrorLevel.Error, LearnErrorCode.TripleCrown_DrySyncError, file: null, result.Message);
            }

            return result.IsValid;
        }

        private static bool ValidateHierarchyInOtherLocales(
            bool isValid,
            List<IValidateModel> hierarchyItems,
            LearnValidationConfig config,
            LearnValidationHelper learnValidationHelper,
            LearnValidationLogger logger)
        {
            var tokenValidator = new TokenValidator(config.DependencyFilePath, hierarchyItems, config.DocsetPath, config.FallbackDocsetPath, logger);
            isValid = isValid && tokenValidator.Validate();
            var partialPublishProcessor = new InvalidFilesProvider(hierarchyItems, learnValidationHelper, logger);
            var filesToDelete = partialPublishProcessor.GetFilesToDelete();
            HierarchyGenerator.GenerateHierarchy(hierarchyItems, config.DocsetOutputPath);
            RemoveInvalidPublishItems(config.PublishFilePath, filesToDelete, logger);

            return isValid;
        }

        private static async Task<ValidationResult> TryDrySync(
            string branch,
            string locale,
            string docsetName,
            string repoUrl,
            RawHierarchy hierarchy,
            ILearnServiceAccessor learnServiceAccessor)
        {
            try
            {
                return await DrySync(branch, locale, docsetName, repoUrl, hierarchy, learnServiceAccessor);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{PluginName}] exception occurs during dry sync step: {ex}");

                // regard current hierarchy as valid if any unhandled exceptions occurs to avoid blocking build.
                return new ValidationResult(branch, locale, true, "");
            }
        }

        private static async Task<ValidationResult> DrySync(
            string branch,
            string locale,
            string docsetName,
            string repoUrl,
            RawHierarchy hierarchy,
            ILearnServiceAccessor learnServiceAccessor)
        {
            var body = JsonConvert.SerializeObject(new DrySyncMessage
            {
                RawHierarchy = hierarchy,
                Locale = locale,
                Branch = branch,
                DocsetName = docsetName,
                RepoUrl = repoUrl,
            });

            Console.WriteLine($"[{PluginName}] start to call dry-sync...");
            var sw = Stopwatch.StartNew();

            var data = await learnServiceAccessor.HierarchyDrySync(body);
            var results = JsonConvert.DeserializeObject<List<ValidationResult>>(data);
            Console.WriteLine($"[{PluginName}] dry-sync done in {sw.ElapsedMilliseconds / 1000}s");

            return results.First(r => string.Equals(r.Locale, Constants.DefaultLocale));
        }

        private static void RemoveInvalidPublishItems(string publishFilePath, HashSet<string> invalidFiles, LearnValidationLogger logger)
        {
            var publishModel = JsonConvert.DeserializeObject<LearnPublishModel>(File.ReadAllText(publishFilePath));
            publishModel.Files.RemoveAll(item => invalidFiles.Contains(item.SourcePath));

            if (logger.HasFileWithError)
            {
                foreach (var item in publishModel.Files)
                {
                    item.HasError = item.HasError || logger.FileHasError(item.SourcePath);
                }
            }
            File.WriteAllText(publishFilePath, JsonConvert.SerializeObject(publishModel));
        }
    }
}