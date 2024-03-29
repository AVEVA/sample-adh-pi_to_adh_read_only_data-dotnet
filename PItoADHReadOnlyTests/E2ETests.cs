﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OSIsoft.Data;
using OSIsoft.Data.Http;
using OSIsoft.Data.Reflection;
using OSIsoft.Identity;
using PItoADHReadOnly;
using Xunit;

namespace PItoADHReadOnlyTests
{
    public class E2ETests
    {
        private static SdsType _type;
        private static SdsStream _stream;

        private static IConfigurationRoot _appSettings;
        private static ISdsMetadataService _metadataService;
        private static ISdsDataService _dataService;

        [Fact]
        public async void TestMainAsync()
        {
            // Initialize Test Data
            ReadAppSettings();
            CreateSDSServices();
            await CreateSDSTypeAndStreamAsync().ConfigureAwait(false);
            await WriteTestDataAsync().ConfigureAwait(false);

            // Run and Assert Program Main Test
            bool result = await Program.MainAsync(true).ConfigureAwait(false);
            Assert.True(result);

            // Delete Test Data
            await TearDownTestResourcesAsync().ConfigureAwait(false);
        }

        private static void ReadAppSettings()
        {
            IConfigurationBuilder builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.test.json", optional: true);
            _appSettings = builder.Build();
        }

        private static void CreateSDSServices()
        {
            Uri uriResource = new (_appSettings["Resource"]);

            AuthenticationHandler authenticationHandler = new (uriResource, _appSettings["ClientId"], _appSettings["ClientSecret"]);
            VerbosityHeaderHandler verbosityHeaderHandler = new ();
            SdsService sdsService = new (uriResource, formatter: null, HttpCompressionMethod.GZip, authenticationHandler, verbosityHeaderHandler);

            string tenantId = _appSettings["TenantId"];
            string namespaceId = _appSettings["NamespaceId"];
            _metadataService = sdsService.GetMetadataService(tenantId, namespaceId);
            _dataService = sdsService.GetDataService(tenantId, namespaceId);
        }

        private static async Task CreateSDSTypeAndStreamAsync()
        {
            SdsType typeToCreate = SdsTypeBuilder.CreateSdsType<PItoADHEvent>();
            typeToCreate.Id = _appSettings["TypeId"];
            _type = await _metadataService.GetOrCreateTypeAsync(typeToCreate).ConfigureAwait(false);

            string streamId = _appSettings["StreamId"];
            SdsStream stream = new ()
            {
                Id = streamId,
                Name = streamId,
                TypeId = _type.Id,
                Description = "This is a test stream used to simulate a PI to OCS stream.",
            };
            _stream = await _metadataService.GetOrCreateStreamAsync(stream).ConfigureAwait(false);
        }

        private static async Task WriteTestDataAsync()
        {
            // Create events with different values and flags
            // Spacing out the timestamps by 1 sec
            DateTime timestamp = DateTime.UtcNow;
            Random rand = new ();

            List<PItoADHEvent> values = new ()
            {
                new PItoADHEvent()
                {
                    Value = rand.Next(80, 120),
                    Timestamp = timestamp,
                },
                new PItoADHEvent()
                {
                    Value = rand.Next(-50, -10),
                    Timestamp = timestamp.Subtract(TimeSpan.FromSeconds(1)),
                },
                new PItoADHEvent()
                {
                    Value = rand.Next(0, 40),
                    IsQuestionable = true,
                    Timestamp = timestamp.Subtract(TimeSpan.FromSeconds(2)),
                },
                new PItoADHEvent()
                {
                    SystemStateCode = 246,
                    DigitalStateName = "I/O Timeout",
                    Timestamp = timestamp.Subtract(TimeSpan.FromSeconds(3)),
                },
            };

            await _dataService.InsertValuesAsync(_stream.Id, values).ConfigureAwait(false);
        }

        private static async Task TearDownTestResourcesAsync()
        {
            await _metadataService.DeleteStreamAsync(_stream.Id).ConfigureAwait(false);
            await _metadataService.DeleteTypeAsync(_type.Id).ConfigureAwait(false);
        }
    }
}
