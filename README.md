# SSIS Salesforce Source Extension

This repository provides a sample SQL Server Integration Services (SSIS) data flow source component that can be used to extract data from Salesforce using OAuth 2.0 authentication and push it into a downstream SQL destination. The component is implemented as a custom pipeline source with a helper connection manager for handling OAuth tokens.

## Project Layout

- `src/SalesforceSource/`
  - `SalesforceSource.csproj`: .NET Framework project for the custom component.
  - `SalesforceConnectionManager.cs`: Implements OAuth 2.0 authentication flows and token caching for Salesforce.
  - `SalesforceSourceComponent.cs`: Implements the SSIS data flow source component that executes SOQL queries and produces output columns dynamically.
  - `SalesforceRestClient.cs`: Lightweight REST client wrapper for the Salesforce REST API.

## Building

The project targets .NET Framework 4.8 and depends on the SSIS runtime assemblies that ship with SQL Server Data Tools. To build locally, open the solution in Visual Studio on Windows with SSIS installed or ensure the following assemblies are available:

- `Microsoft.SqlServer.DTSPipelineWrap`
- `Microsoft.SqlServer.DTSRuntimeWrap`
- `Microsoft.SQLServer.ManagedDTS`

## Usage

1. Build the project to produce `SalesforceSource.dll`.
2. Deploy the assembly to the SSIS pipeline components directory (typically `%ProgramFiles%\Microsoft SQL Server\150\DTS\PipelineComponents`).
3. Register the component in the GAC if required.
4. In an SSIS data flow, add the **Salesforce Source** component and configure the component properties with your Salesforce OAuth credentials, security token, and the desired SOQL query.
5. Connect the output to any downstream transformation or SQL Server destination.

Refer to the XML comments in the source files for additional configuration details.
