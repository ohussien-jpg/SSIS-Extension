using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SalesforceExtension
{
    /// <summary>
    /// Provides helper methods to execute SOQL queries using the Salesforce REST API.
    /// </summary>
    public sealed class SalesforceRestClient
    {
        private readonly SalesforceConnectionManager _connectionManager;
        private readonly string _apiVersion;

        public SalesforceRestClient(SalesforceConnectionManager connectionManager, string apiVersion = "v57.0")
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _apiVersion = apiVersion;
        }

        public async Task<IReadOnlyList<JObject>> QueryAsync(string soql, int batchSize, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(soql))
            {
                throw new ArgumentException("SOQL query cannot be null or whitespace.", nameof(soql));
            }

            await _connectionManager.EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

            var records = new List<JObject>();
            var encodedQuery = Uri.EscapeDataString(soql);
            var instanceUri = new Uri(_connectionManager.InstanceUrl);
            var queryUri = new Uri(instanceUri, $"/services/data/{_apiVersion}/query?q={encodedQuery}{(batchSize > 0 ? $"&batchSize={batchSize}" : string.Empty)}");

            while (true)
            {
                var response = await _connectionManager.GetAsync(queryUri, cancellationToken).ConfigureAwait(false);
                var responseRecords = response.Value<JArray>("records") ?? new JArray();
                foreach (var record in responseRecords)
                {
                    if (record is JObject obj)
                    {
                        records.Add(obj);
                    }
                }

                var done = response.Value<bool?>("done") ?? true;
                if (done)
                {
                    break;
                }

                var next = response.Value<string>("nextRecordsUrl");
                if (string.IsNullOrWhiteSpace(next))
                {
                    break;
                }

                queryUri = new Uri(instanceUri, next);
            }

            return records;
        }
    }
}
