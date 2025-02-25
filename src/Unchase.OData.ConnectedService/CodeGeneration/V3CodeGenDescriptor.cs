﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Updated by Unchase (https://github.com/unchase).
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Data.Services.Design;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Data.Edm.Csdl;
using Microsoft.VisualStudio.ConnectedServices;
using Unchase.OData.ConnectedService.Common;
using Unchase.OData.ConnectedService.Models;
using Constants = Unchase.OData.ConnectedService.Common.Constants;

namespace Unchase.OData.ConnectedService.CodeGeneration
{
    internal class V3CodeGenDescriptor : BaseCodeGenDescriptor
    {
        #region Properties and fields
        private ServiceConfigurationV3 ServiceConfiguration { get; }
        #endregion

        #region Constructors
        public V3CodeGenDescriptor(ConnectedServiceHandlerContext context, Instance serviceInstance)
            : base(context, serviceInstance)
        {
            this.ClientNuGetPackageName = Common.Constants.V3ClientNuGetPackage;
            this.ValueTupleNuGetPackageName = Common.Constants.ValueTupleNuGetPackage;
            this.SimpleODataClientNuGetPackageName = Common.Constants.SimpleODataClientNuGetPackage;
            this.SystemComponentModelAnnotationsNuGetPackageName = Common.Constants.SystemComponentModelAnnotationsNuGetPackage;
            this.ClientDocUri = Common.Constants.V3DocUri;
            this.ServiceConfiguration = base.Instance.ServiceConfig as ServiceConfigurationV3;
        }
        #endregion

        #region Methods

        #region NuGet
        public override async Task AddNugetPackages()
        {
            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Adding Nuget Packages for OData V3...");

            var packageSource = Common.Constants.NuGetOnlineRepository;

            if (this.ServiceConfiguration.IncludeExtensionsT4File)
                await CheckAndInstallNuGetPackage(Common.Constants.NuGetOnlineRepository, this.ValueTupleNuGetPackageName);

            await CheckAndInstallNuGetPackage(Common.Constants.NuGetOnlineRepository, this.SystemComponentModelAnnotationsNuGetPackageName);

            if (this.ServiceConfiguration.IncludeExtensionsT4File && this.ServiceConfiguration.GenerateFunctionImports && this.ServiceConfiguration.FunctionImportsGenerator == Constants.FunctionImportsGenerator.SimpleOData)
                await CheckAndInstallNuGetPackage(Common.Constants.NuGetOnlineRepository, this.SimpleODataClientNuGetPackageName);

            if (packageSource == Common.Constants.NuGetOnlineRepository)
            {
                foreach (var nugetPackage in Common.Constants.V3NuGetPackages)
                    await CheckAndInstallNuGetPackage(packageSource, nugetPackage);
            }
            else
                await CheckAndInstallNuGetPackage(packageSource, this.ClientNuGetPackageName);

            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Nuget Packages for OData V3 were installed.");
        }

        internal async Task CheckAndInstallNuGetPackage(string packageSource, string nugetPackage)
        {
            try
            {
                if (!PackageInstallerServices.IsPackageInstalled(this.Project, nugetPackage))
                {
                    Version packageVersion = null;
                    PackageInstaller.InstallPackage(packageSource, this.Project, nugetPackage, packageVersion, false);

                    await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, $"Nuget Package \"{nugetPackage}\" for OData V3 was added.");
                }
                else
                {
                    await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, $"Nuget Package \"{nugetPackage}\" for OData V3 already installed.");
                }
            }
            catch (Exception ex)
            {
                await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Error, $"Nuget Package \"{nugetPackage}\" for OData V3 not installed. Error: {ex.Message}.");
            }
        }
        #endregion

        public override async Task AddGeneratedClientCode()
        {
            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Generating Client Proxy for OData V3...");

            var generator = new EntityClassGenerator(this.ServiceConfiguration.LanguageOption)
            {
                UseDataServiceCollection = this.ServiceConfiguration.UseDataServiceCollection,
                Version = DataServiceCodeVersion.V3
            };

            var xmlUrlResolver = new XmlUrlResolver
            {
                Credentials = this.UseNetworkCredentials ? new NetworkCredential(this.NetworkCredentialsUserName, this.NetworkCredentialsPassword, this.NetworkCredentialsDomain) : System.Net.CredentialCache.DefaultNetworkCredentials
            };
            if (this.UseWebProxy)
            {
                xmlUrlResolver.Proxy = new WebProxy(this.WebProxyUri, true);
                if (this.UseWebProxyCredentials)
                    xmlUrlResolver.Proxy = new WebProxy(this.WebProxyUri, true, new string[0], new NetworkCredential
                    {
                        UserName = WebProxyNetworkCredentialsUserName,
                        Password = WebProxyNetworkCredentialsPassword,
                        Domain = WebProxyNetworkCredentialsDomain
                    });
                else
                    xmlUrlResolver.Proxy = new WebProxy(this.WebProxyUri);
            }

            var permissionSet = new PermissionSet(System.Security.Permissions.PermissionState.Unrestricted);

            var settings = new XmlReaderSettings()
            {
                XmlResolver = new XmlSecureResolver(xmlUrlResolver, permissionSet)
            };

            using (var reader = XmlReader.Create(this.MetadataUri, settings))
            {
                var tempFile = Path.GetTempFileName();
                var noErrors = true;

                using (var writer = File.CreateText(tempFile))
                {
                    var errors = generator.GenerateCode(reader, writer, this.ServiceConfiguration.NamespacePrefix);
                    await writer.FlushAsync();
                    if (errors != null && errors.Any())
                    {
                        noErrors = false;

                        foreach (var err in errors)
                        {
                            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Warning, err.Message);
                        }

                        await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Warning, "Client Proxy for OData V3 was not generated.");
                    }
                }

                if (noErrors)
                {
                    var ext = this.ServiceConfiguration.LanguageOption == LanguageOption.GenerateCSharpCode
                        ? ".cs"
                        : ".vb";
                    var outputFile = Path.Combine(GetReferenceFileFolder(), this.GeneratedFileNamePrefix + ext);
                    await this.Context.HandlerHelper.AddFileAsync(tempFile, outputFile, new AddFileOptions { OpenOnComplete = this.Instance.ServiceConfig.OpenGeneratedFilesOnComplete });

                    await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Client Proxy for OData V3 was generated.");

                    if (ServiceConfiguration.IncludeExtensionsT4File && this.ServiceConfiguration.LanguageOption == LanguageOption.GenerateCSharpCode)
                        await AddGeneratedClientExtensionsCode(outputFile);
                }
            }
        }

        internal async Task AddGeneratedClientExtensionsCode(string generatedFileName)
        {
            if (!File.Exists(generatedFileName))
            {
                await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Warning, "Client Proxy for OData V3 was not generated.");
                return;
            }

            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Generating Client Proxy extensions class for OData V3...");

            var tempFile = Path.GetTempFileName();
            var t4Folder = Path.Combine(CurrentAssemblyPath, "Templates");

            var proxyClassText = File.ReadAllText(generatedFileName);
            var proxyClassNamespace = Regex.Match(proxyClassText, @"(namespace\s)\w+.+", RegexOptions.IgnoreCase | RegexOptions.Multiline).Value.Trim().Replace("namespace ", string.Empty).Trim();
            var proxyClassName = Regex.Match(proxyClassText, @"(public partial class\s)\w+", RegexOptions.IgnoreCase | RegexOptions.Multiline).Value.Trim().Replace("public partial class ", string.Empty).Trim();

            var functionMethods = string.Empty;
            if (this.ServiceConfiguration.IncludeExtensionsT4File && this.ServiceConfiguration.GenerateFunctionImports)
            {
                try
                {
                    var xmlUrlResolver = new XmlUrlResolver
                    {
                        Credentials = this.UseNetworkCredentials ? new NetworkCredential(this.NetworkCredentialsUserName, this.NetworkCredentialsPassword, this.NetworkCredentialsDomain) : System.Net.CredentialCache.DefaultNetworkCredentials
                    };
                    if (this.UseWebProxy)
                    {
                        xmlUrlResolver.Proxy = new WebProxy(this.WebProxyUri, true);
                        if (this.UseWebProxyCredentials)
                            xmlUrlResolver.Proxy = new WebProxy(this.WebProxyUri, true, new string[0], new NetworkCredential
                            {
                                UserName = WebProxyNetworkCredentialsUserName,
                                Password = WebProxyNetworkCredentialsPassword,
                                Domain = WebProxyNetworkCredentialsDomain
                            });
                        else
                            xmlUrlResolver.Proxy = new WebProxy(this.WebProxyUri);
                    }

                    var settings = new XmlReaderSettings
                    {
                        XmlResolver = new XmlSecureResolver(xmlUrlResolver, new PermissionSet(System.Security.Permissions.PermissionState.Unrestricted))
                    };

                    using (var reader = XmlReader.Create(ServiceConfiguration.Endpoint, settings))
                    {
                        if (EdmxReader.TryParse(reader, out var model, out var parseErrors))
                            functionMethods = FunctionImportsHelper.GetFunctionImportsCode(model, proxyClassNamespace, proxyClassName, ServiceConfiguration.Endpoint.Replace("$metadata", string.Empty), this.ServiceConfiguration.FunctionImportsGenerator);
                        else
                        {
                            foreach (var error in parseErrors)
                                await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Warning, error.ErrorMessage);
                        }
                    }
                }
                catch (Exception ex)
                {
                    await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Error, $"Error: {ex.Message}.");
                }
            }

            using (var writer = File.CreateText(tempFile))
            {
                var text = File.ReadAllText(Path.Combine(t4Folder, Common.Constants.V3ExtensionsT4FileName));
                text = Regex.Replace(text, "(namespace )\\$rootnamespace\\$", "$1" + (string.IsNullOrEmpty(ServiceConfiguration.NamespacePrefix) ? proxyClassNamespace : ServiceConfiguration.NamespacePrefix));
                text = Regex.Replace(text, "\\$proxyClassName\\$", proxyClassName);
                text = Regex.Replace(text, "\\$functionMethods\\$", !string.IsNullOrEmpty(functionMethods) ? functionMethods : string.Empty);
                text = Regex.Replace(text, "(new Uri\\()\"\\$metadataDocumentUri\\$\"\\)\\)", "$1\"" + ServiceConfiguration.Endpoint.Replace("$metadata", string.Empty) + "\"))");
                if (this.ServiceConfiguration.FunctionImportsGenerator != Constants.FunctionImportsGenerator.SimpleOData)
                    text = Regex.Replace(text, "using Simple.OData.Client;", string.Empty);

                await writer.WriteAsync(text);
                await writer.FlushAsync();
            }

            var outputFile = Path.Combine(GetReferenceFileFolder(), this.GeneratedFileNamePrefix + "Extensions.cs");
            await this.Context.HandlerHelper.AddFileAsync(tempFile, outputFile, new AddFileOptions { OpenOnComplete = this.Instance.ServiceConfig.OpenGeneratedFilesOnComplete });

            await this.Context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Client Proxy extensions class for OData V3 was generated.");
        }

        #endregion
    }
}
