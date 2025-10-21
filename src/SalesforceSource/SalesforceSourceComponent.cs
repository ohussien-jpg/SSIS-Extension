using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.SqlServer.Dts.Pipeline;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using Newtonsoft.Json.Linq;

namespace SalesforceExtension
{
    [DtsPipelineComponent(
        DisplayName = "Salesforce Source",
        Description = "Extracts data from Salesforce using OAuth 2.0 authentication.",
        ComponentType = ComponentType.SourceAdapter,
        CurrentVersion = 1)]
    public sealed class SalesforceSourceComponent : PipelineComponent
    {
        private const string PropertyClientId = nameof(SalesforceConnectionOptions.ClientId);
        private const string PropertyClientSecret = nameof(SalesforceConnectionOptions.ClientSecret);
        private const string PropertyUsername = nameof(SalesforceConnectionOptions.Username);
        private const string PropertyPassword = nameof(SalesforceConnectionOptions.Password);
        private const string PropertySecurityToken = nameof(SalesforceConnectionOptions.SecurityToken);
        private const string PropertyUseSandbox = nameof(SalesforceConnectionOptions.UseSandbox);
        private const string PropertyAuthEndpoint = nameof(SalesforceConnectionOptions.AuthEndpoint);
        private const string PropertySoqlQuery = "SoqlQuery";
        private const string PropertyBatchSize = "BatchSize";
        private const int DefaultColumnLength = 4000;

        public override void ProvideComponentProperties()
        {
            base.ProvideComponentProperties();

            ComponentMetaData.Name = "Salesforce Source";
            ComponentMetaData.Description = "Extracts data from Salesforce using OAuth 2.0 authentication.";
            ComponentMetaData.ContactInfo = "Example SSIS Salesforce Source";

            foreach (IDTSInput100 input in ComponentMetaData.InputCollection.Cast<IDTSInput100>().ToArray())
            {
                ComponentMetaData.InputCollection.RemoveObjectByID(input.ID);
            }

            foreach (IDTSOutput100 output in ComponentMetaData.OutputCollection.Cast<IDTSOutput100>().ToArray())
            {
                ComponentMetaData.OutputCollection.RemoveObjectByID(output.ID);
            }

            var salesforceOutput = ComponentMetaData.OutputCollection.New();
            salesforceOutput.Name = "SalesforceOutput";
            salesforceOutput.SynchronousInputID = 0;
            salesforceOutput.ExternalMetadataColumnCollection.IsUsed = true;

            AddProperty(PropertyClientId, string.Empty, "Salesforce connected app client identifier.");
            AddProperty(PropertyClientSecret, string.Empty, "Salesforce connected app client secret.");
            AddProperty(PropertyUsername, string.Empty, "Salesforce username (typically an email address).");
            AddProperty(PropertyPassword, string.Empty, "Salesforce password.");
            AddProperty(PropertySecurityToken, string.Empty, "Salesforce security token appended to the password.");
            AddProperty(PropertyAuthEndpoint, "https://login.salesforce.com", "Base URL for OAuth requests.");
            AddProperty(PropertyUseSandbox, false, "Indicates whether to authenticate against the Salesforce sandbox environment.");
            AddProperty(PropertySoqlQuery, string.Empty, "SOQL query executed against Salesforce.");
            AddProperty(PropertyBatchSize, 200, "Number of records requested per batch (1-2000).");
        }

        public override DTSValidationStatus Validate()
        {
            var errors = new List<string>();

            bool ValidateProperty(string name)
            {
                var property = ComponentMetaData.CustomPropertyCollection[name];
                return property != null && property.Value is string value && !string.IsNullOrWhiteSpace(value);
            }

            if (!ValidateProperty(PropertyClientId)) errors.Add("ClientId is required.");
            if (!ValidateProperty(PropertyClientSecret)) errors.Add("ClientSecret is required.");
            if (!ValidateProperty(PropertyUsername)) errors.Add("Username is required.");
            if (!ValidateProperty(PropertyPassword)) errors.Add("Password is required.");
            if (string.IsNullOrWhiteSpace(ComponentMetaData.CustomPropertyCollection[PropertySoqlQuery]?.Value as string))
            {
                errors.Add("SOQL query must be specified.");
            }

            var batchSizeObj = ComponentMetaData.CustomPropertyCollection[PropertyBatchSize]?.Value;
            if (batchSizeObj is int batchSize && (batchSize < 1 || batchSize > 2000))
            {
                errors.Add("BatchSize must be between 1 and 2000.");
            }

            if (errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    ComponentMetaData.FireError(0, ComponentMetaData.Name, error, string.Empty, 0, out _);
                }

                return DTSValidationStatus.VS_ISBROKEN;
            }

            return base.Validate();
        }

        public override void ReinitializeMetaData()
        {
            base.ReinitializeMetaData();

            if (ComponentMetaData.OutputCollection.Count == 0)
            {
                ProvideComponentProperties();
            }
        }

        public override void PrimeOutput(int outputs, int[] outputIDs, PipelineBuffer[] buffers)
        {
            if (buffers is null || buffers.Length == 0)
            {
                throw new ArgumentNullException(nameof(buffers));
            }

            var buffer = buffers[0];
            var options = BuildConnectionOptions();
            var soqlQuery = ComponentMetaData.CustomPropertyCollection[PropertySoqlQuery]?.Value as string ?? string.Empty;
            var batchSize = Convert.ToInt32(ComponentMetaData.CustomPropertyCollection[PropertyBatchSize]?.Value ?? 200, CultureInfo.InvariantCulture);

            using var cancellationSource = new CancellationTokenSource();
            using var connectionManager = new SalesforceConnectionManager(options);
            var client = new SalesforceRestClient(connectionManager);
            var records = client.QueryAsync(soqlQuery, batchSize, cancellationSource.Token).GetAwaiter().GetResult();

            if (records.Count == 0)
            {
                buffer.SetEndOfRowset();
                return;
            }

            var output = ComponentMetaData.OutputCollection[0];
            var columnIndexes = BuildColumnIndexMap(outputIDs[0], output);

            foreach (var record in records)
            {
                EnsureOutputColumns(record);
                columnIndexes = BuildColumnIndexMap(outputIDs[0], output);

                buffer.AddRow();
                foreach (var property in record.Properties())
                {
                    if (!columnIndexes.TryGetValue(property.Name, out var columnIndex))
                    {
                        continue;
                    }

                    var value = property.Value?.Type == JTokenType.Null ? null : property.Value?.ToString();
                    if (value is null)
                    {
                        buffer.SetNull(columnIndex);
                    }
                    else
                    {
                        buffer.SetString(columnIndex, value);
                    }
                }
            }

            buffer.SetEndOfRowset();
        }

        private SalesforceConnectionOptions BuildConnectionOptions()
        {
            return new SalesforceConnectionOptions
            {
                ClientId = ComponentMetaData.CustomPropertyCollection[PropertyClientId]?.Value as string ?? string.Empty,
                ClientSecret = ComponentMetaData.CustomPropertyCollection[PropertyClientSecret]?.Value as string ?? string.Empty,
                Username = ComponentMetaData.CustomPropertyCollection[PropertyUsername]?.Value as string ?? string.Empty,
                Password = ComponentMetaData.CustomPropertyCollection[PropertyPassword]?.Value as string ?? string.Empty,
                SecurityToken = ComponentMetaData.CustomPropertyCollection[PropertySecurityToken]?.Value as string ?? string.Empty,
                UseSandbox = Convert.ToBoolean(ComponentMetaData.CustomPropertyCollection[PropertyUseSandbox]?.Value ?? false, CultureInfo.InvariantCulture),
                AuthEndpoint = ComponentMetaData.CustomPropertyCollection[PropertyAuthEndpoint]?.Value as string ?? "https://login.salesforce.com"
            };
        }

        private void AddProperty(string name, object defaultValue, string description)
        {
            var property = ComponentMetaData.CustomPropertyCollection.New();
            property.Name = name;
            property.Value = defaultValue;
            property.Description = description;
        }

        private void EnsureOutputColumns(JObject sample)
        {
            var output = ComponentMetaData.OutputCollection[0];
            foreach (var property in sample.Properties())
            {
                var column = FindColumn(output, property.Name);
                if (column != null)
                {
                    continue;
                }

                column = output.OutputColumnCollection.New();
                column.Name = property.Name;
                column.Description = $"Salesforce field {property.Name}";
                column.SetDataTypeProperties(DataType.DT_WSTR, DefaultColumnLength, 0, 0, 0);
            }
        }

        private static IDTSOutputColumn100? FindColumn(IDTSOutput100 output, string name)
        {
            foreach (IDTSOutputColumn100 column in output.OutputColumnCollection)
            {
                if (string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return column;
                }
            }

            return null;
        }

        private Dictionary<string, int> BuildColumnIndexMap(int outputId, IDTSOutput100 output)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (IDTSOutputColumn100 column in output.OutputColumnCollection)
            {
                var index = BufferManager.FindColumnByLineageID(outputId, column.LineageID);
                map[column.Name] = index;
            }

            return map;
        }
    }
}
