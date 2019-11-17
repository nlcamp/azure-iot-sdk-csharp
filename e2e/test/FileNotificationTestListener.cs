﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Devices.E2ETests
{
    public static class FileNotificationTestListener
    {
        private static readonly TimeSpan s_duration = TimeSpan.FromHours(2);
        private static readonly TimeSpan s_interval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan s_checkInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan s_checkDuration = TimeSpan.FromMinutes(1);
        private static readonly TestLogging s_log = TestLogging.GetInstance();

        private static readonly SemaphoreSlim s_lock = new SemaphoreSlim(1, 1);
        private static readonly ConcurrentDictionary<string, FileNotification> s_fileNotifications = new ConcurrentDictionary<string, FileNotification>();
        private static bool s_receiving = false;

        public static async Task InitAsync()
        {
            bool gained = await s_lock.WaitAsync(s_interval).ConfigureAwait(false);
            if (gained)
            {
                try
                {
                    if (!s_receiving)
                    {
                        s_log.WriteLine("Initializing FileNotificationReceiver...");
                        ServiceClient serviceClient = ServiceClient.CreateFromConnectionString(Configuration.IoTHub.ConnectionString);
                        FileNotificationReceiver<FileNotification> fileNotificationReceiver = serviceClient.GetFileNotificationReceiver();
                        s_log.WriteLine("Receiving once to connect FileNotificationReceiver...");
                        await fileNotificationReceiver.ReceiveAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                        s_log.WriteLine("FileNotificationReceiver connected.");
                        _ = StartReceivingLoopAsync(fileNotificationReceiver).ConfigureAwait(false);
                        s_receiving = true;
                    }
                }
                finally
                {
                    s_lock.Release();
                }
            }
        }

        public static async Task VerifyFileNotification(string fileName, string deviceId)
        {
            string key = RetrieveKey(fileName);
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                while (stopwatch.Elapsed < s_checkDuration)
                {
                    bool received = s_fileNotifications.TryRemove(key, out var fileNotification);
                    if (received)
                    {
                        Assert.AreEqual(deviceId, fileNotification.DeviceId);
                        Assert.IsFalse(string.IsNullOrEmpty(fileNotification.BlobUri), "File notification blob uri is null or empty.");
                        return;
                    }
                    await Task.Delay(s_checkInterval).ConfigureAwait(false);
                }
            }
            finally
            {
                stopwatch.Stop();
            }

            Assert.Fail($"FileNotification is not received in {s_checkDuration}: deviceId={deviceId}, blobName={fileName}.");
        }

        private static async Task StartReceivingLoopAsync(FileNotificationReceiver<FileNotification> fileNotificationReceiver)
        {
            s_log.WriteLine("Starting receiving file notification loop...");
            
            CancellationToken cancellationToken = new CancellationTokenSource(s_duration).Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                FileNotification fileNotification = await fileNotificationReceiver.ReceiveAsync(s_interval).ConfigureAwait(false);
                if (fileNotification != null)
                {
                    string key = RetrieveKey(fileNotification.BlobName);
                    s_fileNotifications.TryAdd(key, fileNotification);
                    s_log.WriteLine($"File notification received deviceId={fileNotification.DeviceId}, blobName={fileNotification.BlobName}.");
                }
            }

            s_log.WriteLine("End receiving file notification loop.");
        }

        private static string RetrieveKey(string fileName)
        {
            int index = fileName.LastIndexOf("/", StringComparison.InvariantCultureIgnoreCase);
            if (index > 0)
            {
                return fileName.Substring(index);
            }
            else
            {
                return fileName;
            }
        }
    }
}