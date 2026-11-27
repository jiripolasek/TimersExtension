// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using JPSoftworks.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.TimersExtension;

internal static class Program
{
    [MTAThread]
    public static async Task Main(string[] args)
    {
        await ExtensionHostRunner.CreateBuilder(args,
                new ExtensionHostRunnerParameters
                {
                    PublisherMoniker = "JPSoftworks",
                    ProductMoniker = "TimersExtension",
                    EnableEfficiencyMode = false
                })
            .AddHostedExtensionFactory(context => new TimersExtension(context.ExtensionDisposedEvent))
            .RunAsync();
    }
}