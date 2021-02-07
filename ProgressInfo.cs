namespace Geext.AsyncArtifactCreator
{
	public class ProgressInfo
    {
        public ProgressInfo(double progress, bool isReady)
        {
            Progress = progress;
            IsReady = isReady;
        }

        public double Progress { get; }
        public bool IsReady { get; }
    }
}