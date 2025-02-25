﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Updated by Unchase (https://github.com/unchase).
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ConnectedServices;
using Unchase.OData.ConnectedService.Models;
using Unchase.OData.ConnectedService.Templates;

namespace Unchase.OData.ConnectedService.CodeGeneration
{
    internal class V4CodeGenDescriptor : BaseCodeGenDescriptor
    {
        #region Properties and fields
        private ServiceConfigurationV4 ServiceConfiguration { get; }
        #endregion

        #region Constructors
        public V4CodeGenDescriptor(ConnectedServiceHandlerContext context, Instance serviceInstance)
            : base(context, serviceInstance)
        {
            this.ClientNuGetPackageName = Common.Constants.V4ClientNuGetPackage;
            this.ClientDocUri = Common.Constants.V4DocUri;
            this.ServiceConfiguration = base.Instance.ServiceConfig as ServiceConfigurationV4;
        }
        #endregion

        #region Methods

        #region NuGet
        public override async Task AddNugetPackages()
        {
            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Adding Nuget Packages for OData V4...");

            foreach (var nugetPackage in Common.Constants.V4NuGetPackages)
                await CheckAndInstallNuGetPackage(Common.Constants.NuGetOnlineRepository, nugetPackage);

            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Nuget Packages for OData V4 were installed.");
        }

        internal async Task CheckAndInstallNuGetPackage(string packageSource, string nugetPackage)
        {
            try
            {
                if (!PackageInstallerServices.IsPackageInstalled(this.Project, nugetPackage))
                {
                    Version packageVersion = null;
                    PackageInstaller.InstallPackage(packageSource, this.Project, nugetPackage, packageVersion, false);

                    await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, $"Nuget Package \"{nugetPackage}\" for OData V4 was added.");
                }
                else
                {
                    await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, $"Nuget Package \"{nugetPackage}\" for OData V4 already installed.");
                }
            }
            catch (Exception ex)
            {
                await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Error, $"Nuget Package \"{nugetPackage}\" for OData V4 not installed. Error: {ex.Message}.");
            }
        }
        #endregion

        public override async Task AddGeneratedClientCode()
        {
            if (this.ServiceConfiguration.IncludeT4File)
            {
                await AddT4File();
            }
            else
            {
                await AddGeneratedCSharpCode();
            }
        }

        private async Task AddT4File()
        {
            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Adding T4 file for OData V4...");

            var tempFile = Path.GetTempFileName();
            var t4Folder = Path.Combine(this.CurrentAssemblyPath, "Templates");

            using (var writer = File.CreateText(tempFile))
            {
                var text = File.ReadAllText(Path.Combine(t4Folder, "ODataT4CodeGenerator.tt"));

                text = Regex.Replace(text, "ODataT4CodeGenerator(\\.ttinclude)", this.GeneratedFileNamePrefix + "$1");
                text = Regex.Replace(text, "(public const string MetadataDocumentUri = )\"\";", "$1\"" + ServiceConfiguration.Endpoint + "\";");
                text = Regex.Replace(text, "(public const bool UseDataServiceCollection = ).*;", "$1" + ServiceConfiguration.UseDataServiceCollection.ToString().ToLower(CultureInfo.InvariantCulture) + ";");
                text = Regex.Replace(text, "(public const string NamespacePrefix = )\"\\$rootnamespace\\$\";", "$1\"" + ServiceConfiguration.NamespacePrefix + "\";");
                text = Regex.Replace(text, "(public const string TargetLanguage = )\"OutputLanguage\";", "$1\"CSharp\";");
                text = Regex.Replace(text, "(public const bool EnableNamingAlias = )true;", "$1" + this.ServiceConfiguration.EnableNamingAlias.ToString().ToLower(CultureInfo.InvariantCulture) + ";");
                text = Regex.Replace(text, "(public const bool IgnoreUnexpectedElementsAndAttributes = )true;", "$1" + this.ServiceConfiguration.IgnoreUnexpectedElementsAndAttributes.ToString().ToLower(CultureInfo.InvariantCulture) + ";");

                await writer.WriteAsync(text);
                await writer.FlushAsync();
            }

            var referenceFolder = GetReferenceFileFolder();
            await this.Context.HandlerHelper.AddFileAsync(Path.Combine(t4Folder, "ODataT4CodeGenerator.ttinclude"), Path.Combine(referenceFolder, this.GeneratedFileNamePrefix + ".ttinclude"), new AddFileOptions { OpenOnComplete = this.Instance.ServiceConfig.OpenGeneratedFilesOnComplete });
            await this.Context.HandlerHelper.AddFileAsync(tempFile, Path.Combine(referenceFolder, this.GeneratedFileNamePrefix + ".tt"), new AddFileOptions { OpenOnComplete = this.Instance.ServiceConfig.OpenGeneratedFilesOnComplete });

            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "T4 file for OData V4 was added.");
        }

        private async Task AddGeneratedCSharpCode()
        {
            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Generating Client Proxy for OData V4...");

            var t4CodeGenerator = new ODataT4CodeGenerator
            {
                MetadataDocumentUri = MetadataUri,
                UseDataServiceCollection = this.ServiceConfiguration.UseDataServiceCollection,
                TargetLanguage = ODataT4CodeGenerator.LanguageOption.CSharp,
                IgnoreUnexpectedElementsAndAttributes = this.ServiceConfiguration.IgnoreUnexpectedElementsAndAttributes,
                EnableNamingAlias = this.ServiceConfiguration.EnableNamingAlias,
                NamespacePrefix = this.ServiceConfiguration.NamespacePrefix
            };

            var tempFile = Path.GetTempFileName();

            using (var writer = File.CreateText(tempFile))
            {
                var proxyClassText = t4CodeGenerator.TransformText();
                await writer.WriteAsync(proxyClassText);
                await writer.FlushAsync();
                if (t4CodeGenerator.Errors != null && t4CodeGenerator.Errors.Count > 0)
                {
                    foreach (var err in t4CodeGenerator.Errors)
                        await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Warning, err.ToString());
                }
            }

            var outputFile = Path.Combine(GetReferenceFileFolder(), this.GeneratedFileNamePrefix + ".cs");
            await this.Context.HandlerHelper.AddFileAsync(tempFile, outputFile, new AddFileOptions { OpenOnComplete = this.Instance.ServiceConfig.OpenGeneratedFilesOnComplete });

            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Client Proxy for OData V4 was generated.");
        }
        #endregion
    }
}
