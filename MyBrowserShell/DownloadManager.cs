#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace MyBrowserShell
{
    internal sealed class DownloadManager
    {
        private readonly List<DownloadItem> items = new();

        public IReadOnlyList<DownloadItem> Items => items;

        public DownloadItem Add(string sourceUrl, string resultPath, CoreWebView2DownloadOperation operation)
        {
            var item = new DownloadItem
            {
                FileName = Path.GetFileName(resultPath),
                SourceUrl = sourceUrl,
                ResultPath = resultPath,
                CancelAction = operation.Cancel,
                Status = operation.State.ToString()
            };

            items.Insert(0, item);

            operation.BytesReceivedChanged += (s, e) =>
            {
                item.BytesReceived = operation.BytesReceived;
                item.TotalBytes = operation.TotalBytesToReceive.HasValue
                    ? (long)Math.Min((ulong)long.MaxValue, operation.TotalBytesToReceive.Value)
                    : 0;
            };
            operation.StateChanged += (s, e) =>
            {
                item.Status = operation.State.ToString();
                if (operation.State == CoreWebView2DownloadState.Completed)
                {
                    item.CompletedAtUtc = DateTime.UtcNow;
                    item.CancelAction = null;
                }
                else if (operation.State == CoreWebView2DownloadState.Interrupted)
                {
                    item.FailureReason = operation.InterruptReason.ToString();
                    item.CancelAction = null;
                }
            };

            return item;
        }

        public void Clear() => items.Clear();
    }
}
