using System;
using Octans.Core.Downloaders;

namespace Octans.Core.Downloads;

public sealed record DownloadDequeueResult(QueuedDownload? Download, TimeSpan? SuggestedDelay);
