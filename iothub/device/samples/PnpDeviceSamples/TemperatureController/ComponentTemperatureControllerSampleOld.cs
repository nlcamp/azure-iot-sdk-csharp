﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.Devices.Client.Samples
{
    public class ComponentTemperatureControllerSampleOld
    {
        private const string Thermostat1 = "thermostat1";
        private const string Thermostat2 = "thermostat2";

        private static readonly Random s_random = new();
        
        private readonly DeviceClient _deviceClient;
        private readonly ILogger _logger;

        public ComponentTemperatureControllerSampleOld(DeviceClient deviceClient, ILogger logger)
        {
            _deviceClient = deviceClient ?? throw new ArgumentNullException(nameof(deviceClient), $"{nameof(deviceClient)} cannot be null.");

            if (logger == null)
            {
                using ILoggerFactory loggerFactory = LoggerFactory.Create(builer => builer.AddConsole());
                _logger = loggerFactory.CreateLogger<ComponentTemperatureControllerSampleOld>();
            }
            else
            {
                _logger = logger;
            }
        }

        public async Task PerformOperationsAsync(CancellationToken cancellationToken)
        {
            // Retrieve the device's properties.
            Twin properties = await _deviceClient.GetTwinAsync(cancellationToken: cancellationToken);

            // Verify if the device has previously reported a value for property "maxTempSinceLastReboot" under component "thermostat1".
            // If the expected value has not been previously reported then report it.
            double maxTempSinceLastReboot = 25;
            if (!properties.Properties.Reported.Contains(Thermostat1) || ((JObject)properties.Properties.Reported[Thermostat1]).Value<double>("maxTempSinceLastReboot") != maxTempSinceLastReboot)
            {
                var propertiesToBeUpdated = new TwinCollection
                {
                    ["__t"] = "c",
                    ["maxTempSinceLastReboot"] = maxTempSinceLastReboot
                };
                var componentProperty = new TwinCollection
                {
                    [Thermostat1] = propertiesToBeUpdated
                };
                await _deviceClient.UpdateReportedPropertiesAsync(propertiesToBeUpdated, cancellationToken);
                _logger.LogDebug($"Property: Update - {propertiesToBeUpdated.ToJson()} in KB.");
            }

            // Send telemetry "deviceHealth" under component "thermostat1".
            var deviceHealth = new DeviceHealth
            {
                Status = "running",
                IsStopRequested = false,
            };
            var telemtry = new Dictionary<string, object>()
            {
                ["deviceHealth"] = deviceHealth
            };

            using var message = new Message(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(telemtry)))
            {
                MessageId = s_random.Next().ToString(),
                ContentEncoding = "utf-8",
                ContentType = "application/json",
                ComponentName = Thermostat1
            };

            await _deviceClient.SendEventAsync(message, cancellationToken);
            _logger.LogDebug($"Telemetry: Sent - {JsonConvert.SerializeObject(telemtry)} in KB.");

            // Subscribe and respond to event for writable property "targetTemperature" under component "thermostat1".
            await _deviceClient.SetDesiredPropertyUpdateCallbackAsync(async (desired, userContext) =>
            {
                string propertyName = "targetTemperature";
                if (!desired.Contains(Thermostat1) || !((JObject)desired[Thermostat1]).ContainsKey(propertyName))
                {
                    _logger.LogDebug($"Property: Update - Received a property update which is not implemented.\n{desired.ToJson()}");
                    return;
                }

                double targetTemperature = ((JObject)desired[Thermostat1]).Value<double>(propertyName);

                var propertyPatch = new TwinCollection();
                var componentPatch = new TwinCollection()
                {
                    ["__t"] = "c"
                };
                var temperatureUpdateResponse = new TwinCollection
                {
                    ["value"] = targetTemperature,
                    ["ac"] = (int)StatusCode.Completed,
                    ["av"] = desired.Version,
                    ["ad"] = "The operation completed successfully."
                };
                componentPatch[propertyName] = temperatureUpdateResponse;
                propertyPatch[Thermostat1] = componentPatch;

                _logger.LogDebug($"Property: Received - component=\"{Thermostat1}\", {{ \"{propertyName}\": {targetTemperature}°C }}.");

                await _deviceClient.UpdateReportedPropertiesAsync(propertyPatch, cancellationToken);
                _logger.LogDebug($"Property: Update - \"{propertyPatch.ToJson()}\" is complete.");
            },
            null,
            cancellationToken: cancellationToken);

            // Subscribe and respond to command "updateTemperatureWithDelay" under component "thermostat2".
            await _deviceClient.SetMethodHandlerAsync($"{Thermostat2}*updateTemperatureWithDelay", async (commandRequest, userContext) =>
            {
                try
                {
                    UpdateTemperatureRequest updateTemperatureRequest = JsonConvert.DeserializeObject<UpdateTemperatureRequest>(commandRequest.DataAsJson);

                    _logger.LogDebug($"Command: Received - component=\"{Thermostat2}\"," +
                        $" updating temperature reading to {updateTemperatureRequest.TargetTemperature}°C after {updateTemperatureRequest.Delay} seconds).");
                    await Task.Delay(TimeSpan.FromSeconds(updateTemperatureRequest.Delay));

                    var updateTemperatureResponse = new UpdateTemperatureResponse
                    {
                        TargetTemperature = updateTemperatureRequest.TargetTemperature,
                        Status = (int)StatusCode.Completed
                    };

                    _logger.LogDebug($"Command: component=\"{Thermostat2}\", target temperature {updateTemperatureResponse.TargetTemperature}°C" +
                                $" has {StatusCode.Completed}.");

                    return new MethodResponse(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(telemtry)), (int)StatusCode.Completed);
                }
                catch (Newtonsoft.Json.JsonException ex)
                {
                    _logger.LogDebug($"Command input is invalid: {ex.Message}.");
                    return new MethodResponse((int)StatusCode.BadRequest);
                }
            },
            null,
            cancellationToken);

            Console.ReadKey();
        }
    }
}
