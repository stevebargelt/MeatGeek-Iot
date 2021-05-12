using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Documents.Client;
using Newtonsoft.Json;
using MeatGeek.IoT.Models;

namespace MeatGeek.IoT
{
    public class Chart
    {
        private readonly CosmosClient _cosmosClient;

        // Use Dependency Injection to inject the HttpClientFactory service and Cosmos DB client that were configured in Startup.cs.
        public Chart(CosmosClient cosmosClient)
        {
            _cosmosClient = cosmosClient;
        }
        
        /// <summary>
        /// Get Session Charts
        /// </summary>
        /// <param name="starttime"></param>
        /// <param name="endtime"></param>
        /// <param name="timeseries"></param>
        /// <returns></returns>
        [FunctionName("GetChart")]
        public  async Task<IActionResult> GetChart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "chart/{starttime}/{timeseries:int?}/{endtime:alpha?}")] HttpRequest req, 
            string starttime,
            int? timeseries,
            string? endtime,
            ILogger log)        
        {

            if (starttime == null)
            {
                log.LogInformation($"Start Time (starttime) not found");
                return new NotFoundResult();
            }

            //TODO: try/catch this
            DateTime StartDateTime = DateTime.Parse(starttime, null, System.Globalization.DateTimeStyles.RoundtripKind);

            log.LogInformation("SmokerId = meatgeek2");
            var StatusPartitionKey = $"meatgeek2-{StartDateTime:yyyy-MM}";
            log.LogInformation($"Status PartitionKey = {StatusPartitionKey}");

            DateTime EndDateTime;
            //TODO: try/catch this
            if (String.IsNullOrEmpty(endtime)) {
                EndDateTime = DateTime.UtcNow;
            }
            else {
                EndDateTime = DateTime.Parse(endtime, null, System.Globalization.DateTimeStyles.RoundtripKind);
            }

            log.LogInformation($"StartTime = {StartDateTime} EndTime = {EndDateTime}");

            var container = _cosmosClient.GetContainer("iot", "telemetry");

            Microsoft.Azure.Cosmos.FeedIterator<SmokerStatus> query;
            query = container.GetItemLinqQueryable<SmokerStatus>(requestOptions: new QueryRequestOptions { PartitionKey = new Microsoft.Azure.Cosmos.PartitionKey(StatusPartitionKey) })
                    .Where(p => p.CurrentTime >= StartDateTime
                            && p.CurrentTime <= EndDateTime)                            
                    .ToFeedIterator();

            List<SmokerStatus> SmokerStatuses = new List<SmokerStatus>();
            var count = 0;
            while (query.HasMoreResults)
            {
                foreach(var status in await query.ReadNextAsync())
                {
                    count++;
                    SmokerStatuses.Add(status);
                }
            }
            log.LogInformation("Statuses " + count);

            if (!timeseries.HasValue) {
                return new OkObjectResult(SmokerStatuses);
            }

            if (timeseries > 0 && timeseries <=60)
            {
                TimeSpan interval = new TimeSpan(0, timeseries.Value, 0); 
                List<SmokerStatus> SortedList = SmokerStatuses.OrderBy(o => o.CurrentTime).ToList();
                var result = SortedList.GroupBy(x=> x.CurrentTime.Ticks/interval.Ticks)
                        .Select(x=>x.First());
                return new OkObjectResult(result);
           
            }
            // Return a 400 bad request result to the client with additional information
            return new BadRequestObjectResult("Please pass a timeseries in range of 1 to 60");

        }  
    }
}