using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AsyncArtifactCreator
{
    public abstract class AsyncArtifactCreator<TArtifactRequest, TArtifact, TArtifactIdentity>
    {
        private readonly ConcurrentDictionary<TArtifactIdentity, TArtifact> _createdArtifacts = new();
        private readonly ConcurrentDictionary<TArtifactIdentity, double> _currentProgress = new();
        private readonly Dictionary<TArtifactIdentity, Task> _currentTasks = new();
        private readonly ILogger? _logger;
        private readonly BlockingCollection<TArtifactRequest> _requestsQueue = new();

        private CancellationTokenSource? _jobCancellationTokenSource;
        private Task? _jobTask;

        protected AsyncArtifactCreator(ILogger? logger = null)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _jobCancellationTokenSource = new CancellationTokenSource();
            var jobCancellationToken = _jobCancellationTokenSource.Token;

            _jobTask = Task.Factory.StartNew(() =>
            {
                foreach (var queuedRequest in _requestsQueue.GetConsumingEnumerable(jobCancellationToken))
                {
                    var requestIdentity = GetIdentity(queuedRequest);
                    _currentProgress.TryAdd(requestIdentity, 0.0);
                    try
                    {
                        lock (_currentTasks)
                        {
                            var newTask = Task.Run(async () =>
                                await ProduceArtifact(queuedRequest, jobCancellationToken)
                                    .ContinueWith(artifactTask =>
                                    {
                                        var artifact = artifactTask.Result;
                                        var artifactIdentity = GetIdentity(artifact);
                                        _createdArtifacts.TryAdd(artifactIdentity, artifact);

                                        lock (_currentTasks)
                                        {
                                            _currentTasks.Remove(artifactIdentity);
                                        }
                                    }, jobCancellationToken), jobCancellationToken);

                            _currentTasks.Add(requestIdentity, newTask);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex,
                            "An exception arouse while processing an artifact request with identity {Identity}",
                            requestIdentity);
                    }
                }
            }, TaskCreationOptions.LongRunning);

            _logger?.LogInformation("The async artifact creator has been started");
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _jobCancellationTokenSource?.Cancel();

            await Task.WhenAll(_currentTasks.Values);
            foreach (var key in _currentTasks.Keys)
                _currentTasks.Remove(key);

            if (_jobTask != null)
            {
                await _jobTask;
                _jobTask = null;
            }

            _logger?.LogInformation("The async artifact creator has been stopped");
        }

        public void AddRequestToQueue(TArtifactRequest request)
        {
            _requestsQueue.Add(request);
            _logger?.LogInformation("Added an artifact request with identity {Identity}", GetIdentity(request));
        }

        public double GetProgress(TArtifactIdentity identity)
        {
            var isPending = _currentTasks.ContainsKey(identity);
            var isReady = _createdArtifacts.ContainsKey(identity);
            if (!isPending && !isReady)
                throw new InvalidOperationException("An artifact with the given identity has not been found");

            if (isReady)
                return 1.0;

            return _currentProgress.TryGetValue(identity, out var progress)
                ? progress
                : 0.0;
        }

        public bool IsReady(TArtifactIdentity identity)
        {
            var isReady = _createdArtifacts.ContainsKey(identity);
            var isPending = _currentTasks.ContainsKey(identity);

            if (!isReady && !isPending)
                throw new InvalidOperationException("An artifact with the given identity has not been found");

            return isReady;
        }

        public TArtifact TakeCreatedArtifact(TArtifactIdentity identity)
        {
            if (!IsReady(identity))
                throw new InvalidOperationException("The artifact is not ready yet");

            _createdArtifacts.Remove(identity, out var createdArtifact);
            _logger?.LogInformation("The artifact with identity {Identity} has been taken", identity);

            return createdArtifact;
        }

        protected abstract Task<TArtifact> ProduceArtifact(TArtifactRequest request,
            CancellationToken cancellationToken = default);

        protected abstract TArtifactIdentity GetIdentity(TArtifactRequest request);
        protected abstract TArtifactIdentity GetIdentity(TArtifact artifact);

        protected void ReportProgress(TArtifactIdentity identity, double progress)
        {
            if (progress > 1.0)
                progress = 1.0;

            if (_currentProgress.TryGetValue(identity, out var previousProgress))
                _currentProgress.TryUpdate(identity, progress, previousProgress);
            else
                _currentProgress.TryAdd(identity, progress);
        }
    }
}