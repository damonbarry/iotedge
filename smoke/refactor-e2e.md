# Proposal to refactor our end-to-end tests

## Introduction

What started out as a small app that automated the steps in our ["quickstart" documentation](https://docs.microsoft.com/en-us/azure/iot-edge/quickstart-linux) has necessarily grown into a much larger set of apps, deployment files, and scripts that perform end-to-end verification of several core Azure IoT Edge scenarios. These tests have proven their value, but are difficult to maintain. This document outlines a proposal for simplifying our end-to-end tests and making them more amenable to our future needs.

## History

As mentioned, the original Quickstart executable did one thing: it automated the steps in our "quickstart" documentation. Even in its original form, however, it had 10 optional command-line parameters which allowed it to be used in a number of different environments and scenarios (e.g., a developer's desktop, or the CI environment). As we put the app to use in more end-to-end scenarios, the parameter set steadily increased to its current number, 26.

When we decided it was time to test devices connecting _to_ the edge, we developed LeafDevice, another executable which duplicates a lot of code from Quickstart. It has its own set of 12 command-line parameters.

Additionally, in order to expand its capabilities, we introduced a second "interface" besides the command-line parameters: a JSON deployment file. It allows the app to set up IoT Edge for any number of scenarios beyond the simple core-runtime-plus-temp-sensor configuration, and would have been very difficult to accomplish through more command-line parameters.

Finally, a number of bash and PowerShell scripts have been developed--one for each test scenario--which set up and use the app in our test environment. They also do appropriate cleanup and collection of logs. These scripts are saved in the DevOps pipeline definition itself rather than in our source control, although we hope to migrate soon to YAML files in source control. There are 39 scripts for Linux, ranging from 12-140 lines in length (avg 66 lines), with a high degree of duplication between scripts. The various PowerShell scripts were recently combined into a single script of 711 lines, with its own set of command line parameters.

## Proposal

Improvements are proposed in two areas:

1. **Test setup/teardown.** The end-to-end test pipeline should be converted to a YAML build and placed under source control. Common functionality should be refactored into [DevOps YAML templates](https://docs.microsoft.com/en-us/azure/devops/pipelines/process/templates) (job or step, as appropriate) to minimize duplication. This would also mean that the unified PowerShell script would be broken into smaller pieces (although the effort of unifying the scripts is not wasted, as it makes identifying and creating reusable chunks of functionality much easier).

2. **Test execution.** Rather than maintaining one or two binaries with large command-line interfaces which all tests call, existing functionality should be refactored into a shared library of common tools and routines. Individual tests can then be written as code that draws from the library to wire up the functionality needed for that test. The test code would go into the Main function of a small .NET Core app, although it could just as easily become a test case in a framework. In this form, the confusing combination of command-line arguments is replaced by a sequence of coded steps, and all you see in the YAML is the invocation of a test binary by name.

## Considerations

Once of the original goals of the Quickstart binary was that you could run it from your desktop with few-or-no arguments to quickly validate the most basic functionality of our product. You would lost this ability if we replaced the app with a library. However, in reality people aren't running Quickstart from their desktops much these days, probably because we now have the end-to-end tests in place. Also, one could easily use the library to write such an app--perhaps we'd still have a binary called Quickstart(.exe) for just that purpose.
