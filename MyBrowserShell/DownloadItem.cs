#nullable enable
using Microsoft.Web.WebView2.Core;
using System;

namespace MyBrowserShell
{
    internal sealed class DownloadItem
    {
        public string FileName { get; set; } = "Download";
        public string SourceUrl { get; set; } = "";
        public string ResultPath { get; set; } = "";
        public string Status { get; set; } = "Starting";
        public long BytesReceived { get; set; }
        public long TotalBytes { get; set; }
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAtUtc { get; set; }
        public string FailureReason { get; set; } = "";
        public CoreWebView2DownloadOperation? Operation { get; set; }

        public int ProgressPercent =>
            TotalBytes > 0 ? (int)Math.Min(100, BytesReceived * 100 / TotalBytes) : 0;

        public bool CanCancel =>
            Operation != null && Operation.State == CoreWebView2DownloadState.InProgress;

        public bool IsCompleted =>
            Operation?.State == CoreWebView2DownloadState.Completed || CompletedAtUtc.HasValue;

        public bool FileExists =>
            !string.IsNullOrWhiteSpace(ResultPath) && System.IO.File.Exists(ResultPath);
    }
}
