// Copyright 2020 Google Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// [START dlp_numerical_stats]

using Google.Api.Gax.ResourceNames;
using Google.Cloud.Dlp.V2;
using Google.Cloud.PubSub.V1;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Google.Cloud.Dlp.V2.Action.Types;
using static Google.Cloud.Dlp.V2.PrivacyMetric.Types;

public class RiskAnalysisCreateNumericalStats
{
    public static AnalyzeDataSourceRiskDetails.Types.NumericalStatsResult NumericalStats(
        string callingProjectId,
        string tableProjectId,
        string datasetId,
        string tableId,
        string topicId,
        string subscriptionId,
        string columnName)
    {
        var dlp = DlpServiceClient.Create();

        // Construct + submit the job
        var config = new RiskAnalysisJobConfig
        {
            PrivacyMetric = new PrivacyMetric
            {
                NumericalStatsConfig = new NumericalStatsConfig
                {
                    Field = new FieldId { Name = columnName }
                }
            },
            SourceTable = new BigQueryTable
            {
                ProjectId = tableProjectId,
                DatasetId = datasetId,
                TableId = tableId
            },
            Actions =
                {
                    new Google.Cloud.Dlp.V2.Action
                    {
                        PubSub = new PublishToPubSub
                        {
                            Topic = $"projects/{callingProjectId}/topics/{topicId}"
                        }
                    }
                }
        };

        var submittedJob = dlp.CreateDlpJob(
            new CreateDlpJobRequest
            {
                ParentAsProjectName = new ProjectName(callingProjectId),
                RiskJob = config
            });

        // Listen to pub/sub for the job
        var subscriptionName = new SubscriptionName(callingProjectId, subscriptionId);
        var subscriber = SubscriberClient.CreateAsync(
            subscriptionName).Result;

        // SimpleSubscriber runs your message handle function on multiple
        // threads to maximize throughput.
        var done = new ManualResetEventSlim(false);
        subscriber.StartAsync((PubsubMessage message, CancellationToken cancel) =>
        {
            if (message.Attributes["DlpJobName"] == submittedJob.Name)
            {
                Thread.Sleep(500); // Wait for DLP API results to become consistent
                done.Set();
                return Task.FromResult(SubscriberClient.Reply.Ack);
            }
            else
            {
                return Task.FromResult(SubscriberClient.Reply.Nack);
            }
        });

        done.Wait(TimeSpan.FromMinutes(10)); // 10 minute timeout; may not work for large jobs
        subscriber.StopAsync(CancellationToken.None).Wait();

        // Process results
        var resultJob = dlp.GetDlpJob(
            new GetDlpJobRequest
            {
                DlpJobName = DlpJobName.Parse(submittedJob.Name)
            });

        var result = resultJob.RiskDetails.NumericalStatsResult;

        // 'UnpackValue(x)' is a prettier version of 'x.toString()'
        Console.WriteLine($"Value Range: [{UnpackValue(result.MinValue)}, {UnpackValue(result.MaxValue)}]");
        var lastValue = string.Empty;
        for (var quantile = 0; quantile < result.QuantileValues.Count; quantile++)
        {
            var currentValue = UnpackValue(result.QuantileValues[quantile]);
            if (lastValue != currentValue)
            {
                Console.WriteLine($"Value at {quantile + 1}% quantile: {currentValue}");
            }
            lastValue = currentValue;
        }

        return result;
    }

    public static string UnpackValue(Value protoValue)
    {
        var jsonValue = JsonConvert.DeserializeObject<Dictionary<string, object>>(protoValue.ToString());
        return jsonValue.Values.ElementAt(0).ToString();
    }
}
// [END dlp_numerical_stats]
