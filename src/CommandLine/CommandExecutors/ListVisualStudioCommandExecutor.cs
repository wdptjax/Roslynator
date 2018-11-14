﻿// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Build.Locator;
using static Roslynator.Logger;

namespace Roslynator.CommandLine
{
    internal class ListVisualStudioCommandExecutor
    {
        public ListVisualStudioCommandExecutor(ListVisualStudioCommandLineOptions options)
        {
            Options = options;
        }

        public ListVisualStudioCommandLineOptions Options { get; set; }

        public CommandResult Execute()
        {
            int count = 0;
            foreach (VisualStudioInstance instance in MSBuildLocator.QueryVisualStudioInstances())
            {
                WriteLine($"{instance.Name} {instance.Version}", Verbosity.Normal);
                WriteLine($"  Visual Studio Path: {instance.VisualStudioRootPath}", Verbosity.Detailed);
                WriteLine($"  MSBuild Path:       {instance.MSBuildPath}", Verbosity.Detailed);

                count++;
            }

            WriteLine(Verbosity.Minimal);
            WriteLine($"{count} Visual Studio {((count == 1) ? "installation" : "installations")} found", ConsoleColor.Green, Verbosity.Minimal);
            WriteLine(Verbosity.Minimal);

            return CommandResult.Success;
        }
    }
}