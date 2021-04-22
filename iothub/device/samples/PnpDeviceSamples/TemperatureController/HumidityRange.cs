﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace Microsoft.Azure.Devices.Client.Samples
{
    internal class HumidityRange
    {
        [JsonPropertyName("maxTemp")]
        public double MaxTemperature { get; set; }

        [JsonPropertyName("minTemp")]
        public double MinTemperature { get; set; }
    }
}
