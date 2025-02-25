﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Updated by Unchase (https://github.com/unchase).
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ConnectedServices;
using Unchase.OData.ConnectedService.CodeGeneration;
using Unchase.OData.ConnectedService.Common;
using Unchase.OData.ConnectedService.Models;

namespace Unchase.OData.ConnectedService
{
    [ConnectedServiceHandlerExport("Unchase.OData.ConnectedService", AppliesTo = "VB | CSharp | Web")]
    internal class Handler : ConnectedServiceHandler
    {
        #region Methods
        public override async Task<AddServiceInstanceResult> AddServiceInstanceAsync(ConnectedServiceHandlerContext context, CancellationToken ct)
        {
            await context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Adding service instance...");

            var project = ProjectHelper.GetProjectFromHierarchy(context.ProjectHierarchy);
            var codeGenInstance = (Instance)context.ServiceInstance;

            var codeGenDescriptor = await GenerateCode(codeGenInstance.MetadataTempFilePath, codeGenInstance.ServiceConfig.EdmxVersion, context);

            context.SetExtendedDesignerData<ServiceConfiguration>(codeGenInstance.ServiceConfig);

            await context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Adding service instance complete!");

            return new AddServiceInstanceResult(context.ServiceInstance.Name, new Uri(codeGenDescriptor.ClientDocUri));
        }

        public override async Task<UpdateServiceInstanceResult> UpdateServiceInstanceAsync(ConnectedServiceHandlerContext context, CancellationToken ct)
        {
            await context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Updating service instance...");

            var project = ProjectHelper.GetProjectFromHierarchy(context.ProjectHierarchy);
            var codeGenInstance = (Instance)context.ServiceInstance;

            var codeGenDescriptor = await GenerateCode(codeGenInstance.ServiceConfig.Endpoint, codeGenInstance.ServiceConfig.EdmxVersion, context);
            context.SetExtendedDesignerData<ServiceConfiguration>(codeGenInstance.ServiceConfig);

            await context.Logger.WriteMessageAsync(LoggerMessageCategory.Information, "Updating service instance complete.");

            return new UpdateServiceInstanceResult { GettingStartedDocument = new GettingStartedDocument(new Uri(codeGenDescriptor.ClientDocUri)) };
        }

        private static async Task<BaseCodeGenDescriptor> GenerateCode(string metadataUri, Version edmxVersion, ConnectedServiceHandlerContext context)
        {
            var instance = (Instance)context.ServiceInstance;
            BaseCodeGenDescriptor codeGenDescriptor;

            if (edmxVersion == Common.Constants.EdmxVersion1
                || edmxVersion == Common.Constants.EdmxVersion2
                || edmxVersion == Common.Constants.EdmxVersion3)
            {
                codeGenDescriptor = new V3CodeGenDescriptor(context, instance);
            }
            else if (edmxVersion == Common.Constants.EdmxVersion4)
            {
                codeGenDescriptor = new V4CodeGenDescriptor(context, instance);
            }
            else
            {
                throw new Exception(string.Format(CultureInfo.InvariantCulture, "Not supported Edmx Version{0}", (edmxVersion != null ? $" {edmxVersion}." : ". Try with Endpoint ends \"/$metadata\".")));
            }

            var codeGenInstance = (Instance)context.ServiceInstance;
            codeGenDescriptor.UseNetworkCredentials = codeGenInstance.ServiceConfig.UseNetworkCredentials;
            codeGenDescriptor.NetworkCredentialsUserName = codeGenInstance.ServiceConfig.NetworkCredentialsUserName;
            codeGenDescriptor.NetworkCredentialsPassword = codeGenInstance.ServiceConfig.NetworkCredentialsPassword;
            codeGenDescriptor.NetworkCredentialsDomain = codeGenInstance.ServiceConfig.NetworkCredentialsDomain;

            codeGenDescriptor.UseWebProxy = codeGenInstance.ServiceConfig.UseWebProxy;
            codeGenDescriptor.UseWebProxyCredentials = codeGenInstance.ServiceConfig.UseWebProxyCredentials;
            codeGenDescriptor.WebProxyNetworkCredentialsUserName = codeGenInstance.ServiceConfig.WebProxyNetworkCredentialsUserName;
            codeGenDescriptor.WebProxyNetworkCredentialsPassword = codeGenInstance.ServiceConfig.WebProxyNetworkCredentialsPassword;
            codeGenDescriptor.WebProxyNetworkCredentialsDomain = codeGenInstance.ServiceConfig.WebProxyNetworkCredentialsDomain;
            codeGenDescriptor.WebProxyUri = codeGenInstance.ServiceConfig.WebProxyUri;

            await codeGenDescriptor.AddNugetPackages();
            await codeGenDescriptor.AddGeneratedClientCode();

            return codeGenDescriptor;
        }
        #endregion
    }
}
